// src/Infrastructure/Delivery/DdbMessageRetentionCleaner.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ECS.Model;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Periodic/one-off cleaner that removes old message items from the shared DynamoDB table.
    /// Intended to remove delivered/failed messages (or messages with TTL) after a retention period.
    /// 
    /// Table assumptions (shared single-table):
    ///  - PK: id (S)
    ///  - type = "message"
    ///  - ttl (N) = unix epoch seconds (optional)
    ///  - deliveredAtUtc (N) = ticks (optional)
    ///  - failedAtUtc (N) = ticks (optional)
    /// 
    /// Usage:
    ///  - Create an instance and call CleanupAsync(retention, ct) periodically from a hosted service or deployment script.
    ///  - The method is idempotent and uses conditional deletes to avoid races.
    /// </summary>
    public sealed class DdbMessageRetentionCleaner : IDisposable
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbMessageRetentionCleaner>? _logger;
        private bool _disposed;

        private readonly IServiceProvider _svc;
        public DdbMessageRetentionCleaner(IServiceProvider svc, ILogger<DdbMessageRetentionCleaner>? logger = null)
        {
            _svc = svc;
            using var scope = svc.GetService<DynamodbService>();
            var ddb = scope?.DdbClient;
            var tbname = scope?.Options.TableName;

            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = string.IsNullOrWhiteSpace(tbname) ? throw new ArgumentNullException(nameof(tbname)) : tbname;
            _logger = logger;
        }

        /// <summary>
        /// Scan the table and remove message items older than the specified retention period.
        /// Returns the number of items successfully removed.
        /// 
        /// Behavior:
        ///  - Considers three possible age indicators:
        ///      * ttl (unix seconds) if present and less than now
        ///      * deliveredAtUtc (ticks) if present and older than cutoff
        ///      * failedAtUtc (ticks) if present and older than cutoff
        ///  - Only items with type = "message" are considered.
        ///  - Deletes are attempted with a conditional expression to avoid deleting items that changed since scan.
        /// </summary>
        /// <param name="retention">Retention timespan; messages older than this will be removed.</param>
        /// <param name="pageSize">Scan page size (DynamoDB Limit). Defaults to 100.</param>
        public async Task<int> CleanupAsync(TimeSpan retention, int pageSize = 100, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (retention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retention));

            var removed = 0;
            var now = DateTimeOffset.UtcNow;
            var nowUnix = now.ToUnixTimeSeconds();
            var cutoffTicks = DateTime.UtcNow.Subtract(retention).Ticks.ToString();

            // Build a scan that returns candidate items. We can't express complex OR conditions easily in a single expression
            // that mixes attribute types, so we scan for type = "message" and project the relevant attributes, then evaluate per-item.
            var scanReq = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#t = :typeVal AND (attribute_exists(ttl) OR attribute_exists(deliveredAtUtc) OR attribute_exists(failedAtUtc))",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#t"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "message" } },
                ProjectionExpression = "id, ttl, deliveredAtUtc, failedAtUtc",
                Limit = pageSize
            };

            try
            {
                do
                {
                    ct.ThrowIfCancellationRequested();
                    var resp = await _ddb.ScanAsync(scanReq, ct).ConfigureAwait(false);
                    if (resp.Items == null || resp.Items.Count == 0)
                    {
                        scanReq.ExclusiveStartKey = resp.LastEvaluatedKey;
                        if (scanReq.ExclusiveStartKey == null || scanReq.ExclusiveStartKey.Count == 0) break;
                        continue;
                    }

                    foreach (var item in resp.Items)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) continue;
                        var id = idAttr.S;

                        // Determine if this item is eligible for deletion
                        var shouldDelete = false;

                        // 1) ttl (unix seconds)
                        if (item.TryGetValue("ttl", out var ttlAttr) && ttlAttr.N != null)
                        {
                            if (long.TryParse(ttlAttr.N, out var ttlVal) && ttlVal < nowUnix)
                            {
                                shouldDelete = true;
                            }
                        }

                        // 2) deliveredAtUtc (ticks)
                        if (!shouldDelete && item.TryGetValue("deliveredAtUtc", out var delAttr) && delAttr.N != null)
                        {
                            if (long.TryParse(delAttr.N, out var delTicks) && delTicks < long.Parse(cutoffTicks))
                            {
                                shouldDelete = true;
                            }
                        }

                        // 3) failedAtUtc (ticks)
                        if (!shouldDelete && item.TryGetValue("failedAtUtc", out var failAttr) && failAttr.N != null)
                        {
                            if (long.TryParse(failAttr.N, out var failTicks) && failTicks < long.Parse(cutoffTicks))
                            {
                                shouldDelete = true;
                            }
                        }

                        if (!shouldDelete) continue;

                        // Attempt conditional delete to ensure we don't remove items that changed since scan.
                        // Condition: type = "message" AND (ttl < :now OR deliveredAtUtc < :cutoff OR failedAtUtc < :cutoff)
                        var delReq = new DeleteItemRequest
                        {
                            TableName = _tableName,
                            Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = id } },
                            ConditionExpression = "#t = :typeVal AND (" +
                                                  "(attribute_exists(ttl) AND ttl < :now) OR " +
                                                  "(attribute_exists(deliveredAtUtc) AND deliveredAtUtc < :cutoff) OR " +
                                                  "(attribute_exists(failedAtUtc) AND failedAtUtc < :cutoff)" +
                                                  ")",
                            ExpressionAttributeNames = new Dictionary<string, string> { ["#t"] = "type" },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":typeVal"] = new AttributeValue { S = "message" },
                                [":now"] = new AttributeValue { N = nowUnix.ToString() },
                                [":cutoff"] = new AttributeValue { N = cutoffTicks }
                            }
                        };

                        try
                        {
                            await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
                            removed++;
                        }
                        catch (ConditionalCheckFailedException)
                        {
                            // Item changed since scan; skip
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to delete message item {Id} during retention cleanup", id);
                        }
                    }

                    scanReq.ExclusiveStartKey = resp.LastEvaluatedKey;
                } while (scanReq.ExclusiveStartKey != null && scanReq.ExclusiveStartKey.Count > 0);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error while running message retention cleanup");
                throw;
            }

            _logger?.LogInformation("Message retention cleanup completed. Removed {Count} items older than {Retention}", removed, retention);
            return removed;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdbMessageRetentionCleaner));
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
