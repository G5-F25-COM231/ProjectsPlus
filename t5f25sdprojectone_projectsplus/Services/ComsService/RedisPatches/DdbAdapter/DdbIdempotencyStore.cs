// src/Infrastructure/Idempotency/DdbIdempotencyStore.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Simple idempotency marker store using the shared DynamoDB table (PK = "id", "type" discriminator).
    /// Marker item shape:
    ///   id = "idem:{key}"
    ///   type = "idem"
    ///   messageId = "<messageId>"
    ///   createdAtUtc = <ticks>
    ///   ttl = <unix epoch seconds>
    /// 
    /// Methods are intentionally minimal: create-if-not-exists, lookup, and remove.
    /// </summary>
    public sealed class DdbIdempotencyStore : IDisposable
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbIdempotencyStore>? _logger;
        private bool _disposed;

        private readonly IServiceProvider _svc;
        public DdbIdempotencyStore(IServiceProvider svc, ILogger<DdbIdempotencyStore>? logger = null)
        {
            _svc = svc;
            using var scope = svc.GetService<DynamodbService>();
            var ddb = scope?.DdbClient;
            var tbname = scope?.Options.TableName;

            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = tbname ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger;
        }

        private static string MarkerKey(string idempotencyKey) => $"idem:{idempotencyKey}";

        /// <summary>
        /// Try to create an idempotency marker for the given key and messageId.
        /// Returns true if the marker was created; false if a marker already exists.
        /// ttlUnixSeconds is optional; if null, no ttl attribute is written.
        /// </summary>
        public async Task<bool> TryCreateMarkerAsync(string idempotencyKey, string messageId, long? ttlUnixSeconds = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentNullException(nameof(idempotencyKey));
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));

            var now = DateTime.UtcNow;
            var item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = MarkerKey(idempotencyKey) },
                ["type"] = new AttributeValue { S = "idem" },
                ["messageId"] = new AttributeValue { S = messageId },
                ["createdAtUtc"] = new AttributeValue { N = now.Ticks.ToString() }
            };

            if (ttlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };

            var putReq = new PutItemRequest
            {
                TableName = _tableName,
                Item = item,
                ConditionExpression = "attribute_not_exists(id)"
            };

            try
            {
                await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error creating idempotency marker for key {Key}", idempotencyKey);
                throw;
            }
        }

        /// <summary>
        /// Get the messageId associated with an idempotency key, or null if none.
        /// </summary>
        public async Task<string?> GetMessageIdForKeyAsync(string idempotencyKey, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentNullException(nameof(idempotencyKey));

            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = MarkerKey(idempotencyKey) } },
                ProjectionExpression = "messageId, type"
            };

            try
            {
                var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
                if (resp.Item == null || resp.Item.Count == 0) return null;
                if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "idem") return null;
                if (resp.Item.TryGetValue("messageId", out var msg) && msg.S != null) return msg.S;
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error reading idempotency marker for key {Key}", idempotencyKey);
                throw;
            }
        }

        /// <summary>
        /// Remove an idempotency marker. Returns true if removed, false if not present.
        /// </summary>
        public async Task<bool> RemoveMarkerAsync(string idempotencyKey, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentNullException(nameof(idempotencyKey));

            var delReq = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = MarkerKey(idempotencyKey) } },
                ConditionExpression = "#type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "idem" } }
            };

            try
            {
                await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error deleting idempotency marker for key {Key}", idempotencyKey);
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdbIdempotencyStore));
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
