// src/Infrastructure/Enqueue/DdbMessageEnqueuer.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ECS.Model;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// DynamoDB implementation of IMessageEnqueuer.
    /// Uses a single existing table with PK = "id" and a "type" discriminator.
    /// Supports optional idempotency via an "idem:{key}" marker item created in a transaction.
    /// </summary>
    public sealed class DdbMessageEnqueuer : IDbMessageEnqueuer
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbMessageEnqueuer>? _logger;
        private readonly int _messageRetentionMinutes;
        private bool _disposed;

        private readonly IServiceProvider _services;

        public DdbMessageEnqueuer(IServiceProvider services, int messageRetentionMinutes = 60 * 24 * 7)
        {
            _services = services;
            using var scope = _services.CreateScope();
            var ddb = scope.ServiceProvider.GetRequiredService<DynamodbService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DdbMessageEnqueuer>>();

            _ddb = ddb.DdbClient ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = ddb.Options.TableName ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger;
            _messageRetentionMinutes = messageRetentionMinutes;
        }

        public async Task<string> EnqueueAsync(string targetType, string targetId, string payload, string? idempotencyKey = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetType)) throw new ArgumentNullException(nameof(targetType));
            if (string.IsNullOrWhiteSpace(targetId)) throw new ArgumentNullException(nameof(targetId));
            if (payload is null) throw new ArgumentNullException(nameof(payload));

            var msgId = $"message:{Guid.NewGuid():D}";
            var now = DateTime.UtcNow;
            var ttl = DateTimeOffset.UtcNow.AddMinutes(_messageRetentionMinutes).ToUnixTimeSeconds();

            var messageItem = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = msgId },
                ["type"] = new AttributeValue { S = "message" },
                ["targetType"] = new AttributeValue { S = targetType },
                ["targetId"] = new AttributeValue { S = targetId },
                ["payload"] = new AttributeValue { S = payload },
                ["status"] = new AttributeValue { S = "Queued" },
                ["attempts"] = new AttributeValue { N = "0" },
                ["createdAtUtc"] = new AttributeValue { N = now.Ticks.ToString() },
                ["ttl"] = new AttributeValue { N = ttl.ToString() }
            };

            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                var idemId = $"idem:{idempotencyKey}";
                var idemItem = new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = idemId },
                    ["type"] = new AttributeValue { S = "idem" },
                    ["messageId"] = new AttributeValue { S = msgId },
                    ["createdAtUtc"] = new AttributeValue { N = now.Ticks.ToString() },
                    ["ttl"] = new AttributeValue { N = ttl.ToString() }
                };

                var transactItems = new List<TransactWriteItem>
                {
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = _tableName,
                            Item = idemItem,
                            ConditionExpression = "attribute_not_exists(id)"
                        }
                    },
                    new TransactWriteItem
                    {
                        Put = new Put
                        {
                            TableName = _tableName,
                            Item = messageItem
                        }
                    }
                };

                try
                {
                    await _ddb.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = transactItems }, ct).ConfigureAwait(false);
                    return msgId;
                }
                catch (TransactionCanceledException ex)
                {
                    _logger?.LogDebug(ex, "TransactWrite failed for idempotency key {Key}", idempotencyKey);
                    try
                    {
                        var getReq = new GetItemRequest
                        {
                            TableName = _tableName,
                            Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = idemId } },
                            ProjectionExpression = "messageId"
                        };
                        var getResp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
                        if (getResp.Item != null && getResp.Item.TryGetValue("messageId", out var existingMsgIdAttr) && existingMsgIdAttr.S != null)
                        {
                            return existingMsgIdAttr.S;
                        }
                    }
                    catch (Exception getEx)
                    {
                        _logger?.LogWarning(getEx, "Failed to read existing idempotency marker for key {Key}", idempotencyKey);
                    }

                    throw;
                }
            }
            else
            {
                var putReq = new PutItemRequest
                {
                    TableName = _tableName,
                    Item = messageItem
                };

                await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);
                return msgId;
            }
        }

        public async Task<string[]> EnqueueBatchAsync((string targetType, string targetId, string payload, string? idempotencyKey)[] items, CancellationToken ct = default)
        {
            if (items == null || items.Length == 0) return Array.Empty<string>();

            var results = new List<string>(items.Length);
            foreach (var it in items)
            {
                ct.ThrowIfCancellationRequested();
                var id = await EnqueueAsync(it.targetType, it.targetId, it.payload, it.idempotencyKey, ct).ConfigureAwait(false);
                results.Add(id);
            }

            return results.ToArray();
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
