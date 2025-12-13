// src/Infrastructure/Delivery/DeliveryWorker.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Background worker that claims durable messages from DynamoDB and delivers them.
    /// - Claims atomically using UpdateItem with a ConditionExpression (status == "Queued")
    /// - Routes to local dispatcher when ownerInstance == local instance id
    /// - Forwards to remote instance via RemoteForwarder when ownerInstance != local
    /// - Updates message status to Delivered or Failed (with attempts and lastAttemptAtUtc)
    /// 
    /// This implementation assumes the single Dynamo table uses PK = "id" and a "type" discriminator.
    /// </summary>
    public sealed class DeliveryWorker : BackgroundService
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly string _instanceId;
        private readonly IInstanceRegistry _registry;
        private readonly DdbBackedConnectionManager _connectionManager;
        private readonly IRemoteForwarder _remoteForwarder;
        private readonly Func<string, string, Task> _deliverLocalAsync;
        private readonly ILogger<DeliveryWorker> _logger;
        private readonly int _pageSize;
        private readonly TimeSpan _pollDelay;

        private readonly IServiceProvider _svc;
        public DeliveryWorker(
            IServiceProvider svc,
            IAmazonDynamoDB? ddb,
            string? tableName,
            string instanceId,
            IInstanceRegistry registry,
            DdbBackedConnectionManager connectionManager,
            IRemoteForwarder remoteForwarder,
            Func<string, string, Task> deliverLocalAsync,
            ILogger<DeliveryWorker> logger,
            int pageSize = 25,
            TimeSpan? pollDelay = null)
        {
            _svc = svc;
            using var scope = svc.GetService<DynamodbService>();
            ddb ??= scope?.DdbClient;
            tableName ??= scope?.Options.TableName;

            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
            _remoteForwarder = remoteForwarder ?? throw new ArgumentNullException(nameof(remoteForwarder));
            _deliverLocalAsync = deliverLocalAsync ?? throw new ArgumentNullException(nameof(deliverLocalAsync));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pageSize = Math.Max(1, pageSize);
            _pollDelay = pollDelay ?? TimeSpan.FromMilliseconds(500);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DeliveryWorker starting on instance {InstanceId}", _instanceId);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Scan for queued messages (type = "message" AND status = "Queued")
                    var scanReq = new ScanRequest
                    {
                        TableName = _tableName,
                        FilterExpression = "#t = :typeVal AND #s = :queued",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#t"] = "type",
                            ["#s"] = "status"
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":typeVal"] = new AttributeValue { S = "message" },
                            [":queued"] = new AttributeValue { S = "Queued" }
                        },
                        Limit = _pageSize,
                        ProjectionExpression = "id, targetType, targetId, payload, attempts"
                    };

                    var scanResp = await _ddb.ScanAsync(scanReq, stoppingToken).ConfigureAwait(false);
                    if (scanResp.Items == null || scanResp.Items.Count == 0)
                    {
                        await Task.Delay(_pollDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var item in scanResp.Items)
                    {
                        stoppingToken.ThrowIfCancellationRequested();

                        if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) continue;
                        var messageId = idAttr.S;

                        // Attempt to claim the message atomically
                        var claimed = await TryClaimMessageAsync(messageId, stoppingToken).ConfigureAwait(false);
                        if (!claimed) continue; // someone else claimed it

                        // Read full item to get payload/target info (could be returned by UpdateItem but we keep simple)
                        var getReq = new GetItemRequest
                        {
                            TableName = _tableName,
                            Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } }
                        };
                        var getResp = await _ddb.GetItemAsync(getReq, stoppingToken).ConfigureAwait(false);
                        if (getResp.Item == null || getResp.Item.Count == 0)
                        {
                            _logger.LogWarning("Claimed message {MessageId} disappeared", messageId);
                            continue;
                        }

                        getResp.Item.TryGetValue("targetType", out var targetTypeAttr);
                        getResp.Item.TryGetValue("targetId", out var targetIdAttr);
                        getResp.Item.TryGetValue("payload", out var payloadAttr);

                        var targetType = targetTypeAttr?.S ?? string.Empty;
                        var targetId = targetIdAttr?.S ?? string.Empty;
                        var payload = payloadAttr?.S ?? string.Empty;

                        try
                        {
                            // Determine owner for connection targets
                            string? ownerInstance = null;
                            if (string.Equals(targetType, "connection", StringComparison.OrdinalIgnoreCase))
                            {
                                ownerInstance = await _registry.GetOwnerForConnectionAsync(targetId, stoppingToken).ConfigureAwait(false);
                            }
                            else if (string.Equals(targetType, "user", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(targetType, "room", StringComparison.OrdinalIgnoreCase))
                            {
                                // For user/room targets, we attempt to deliver to each connection in the room or user's connections.
                                // For simplicity here, treat as forwarding to a remote forwarder or local dispatcher via connection manager lookup.
                                // Implementations can extend this block to enumerate connections.
                                ownerInstance = await _registry.GetOwnerForConnectionAsync(targetId, stoppingToken).ConfigureAwait(false);
                            }

                            if (!string.IsNullOrEmpty(ownerInstance) && ownerInstance != _instanceId)
                            {
                                // Forward to remote instance
                                var inst = await _registry.GetInstanceAsync(ownerInstance, stoppingToken).ConfigureAwait(false);
                                if (inst != null && !string.IsNullOrEmpty(inst.Address))
                                {
                                    await _remoteForwarder.ForwardMessageAsync(inst.Address, messageId, targetType, targetId, payload, stoppingToken).ConfigureAwait(false);
                                }
                                else
                                {
                                    _logger.LogWarning("Owner instance {Owner} for message {MessageId} not found or has no address", ownerInstance, messageId);
                                    await MarkMessageFailedAsync(messageId, "OwnerNotFound", stoppingToken).ConfigureAwait(false);
                                    continue;
                                }
                            }
                            else
                            {
                                // Local delivery
                                await _deliverLocalAsync(targetId, payload).ConfigureAwait(false);
                            }

                            // Mark delivered (remove or set status Delivered)
                            await MarkMessageDeliveredAsync(messageId, stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Delivery failed for message {MessageId}", messageId);
                            await MarkMessageFailedAsync(messageId, ex.Message, stoppingToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "DeliveryWorker encountered an error; sleeping briefly");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("DeliveryWorker stopping on instance {InstanceId}", _instanceId);
        }

        /// <summary>
        /// Try to claim a message by setting status = Delivering, ownerInstance = this instance, increment attempts and set lastAttemptAtUtc.
        /// Returns true if claim succeeded.
        /// </summary>
        private async Task<bool> TryClaimMessageAsync(string messageId, CancellationToken ct)
        {
            var nowTicks = DateTime.UtcNow.Ticks.ToString();
            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } },
                UpdateExpression = "SET #s = :delivering, ownerInstance = :owner, lastAttemptAtUtc = :now ADD attempts :one",
                ConditionExpression = "#type = :typeVal AND #s = :queued",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#s"] = "status",
                    ["#type"] = "type"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":delivering"] = new AttributeValue { S = "Delivering" },
                    [":owner"] = new AttributeValue { S = _instanceId },
                    [":now"] = new AttributeValue { N = nowTicks },
                    [":one"] = new AttributeValue { N = "1" },
                    [":typeVal"] = new AttributeValue { S = "message" },
                    [":queued"] = new AttributeValue { S = "Queued" }
                },
                ReturnValues = "ALL_NEW"
            };

            try
            {
                await _ddb.UpdateItemAsync(updateReq, ct).ConfigureAwait(false);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        private async Task MarkMessageDeliveredAsync(string messageId, CancellationToken ct)
        {
            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } },
                UpdateExpression = "SET #s = :delivered, deliveredAtUtc = :now REMOVE ownerInstance",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":delivered"] = new AttributeValue { S = "Delivered" }, [":now"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() } }
            };

            try
            {
                await _ddb.UpdateItemAsync(updateReq, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark message {MessageId} as Delivered", messageId);
            }
        }

        private async Task MarkMessageFailedAsync(string messageId, string reason, CancellationToken ct)
        {
            // Increment attempts already done at claim time. Set status to Failed if attempts exceed threshold, otherwise set back to Queued with backoff.
            const int maxAttempts = 5;
            var nowTicks = DateTime.UtcNow.Ticks.ToString();

            // Read attempts
            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } },
                ProjectionExpression = "attempts"
            };

            try
            {
                var getResp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
                var attempts = 0;
                if (getResp.Item != null && getResp.Item.TryGetValue("attempts", out var a) && a.N != null)
                {
                    int.TryParse(a.N, out attempts);
                }

                if (attempts >= maxAttempts)
                {
                    var upd = new UpdateItemRequest
                    {
                        TableName = _tableName,
                        Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } },
                        UpdateExpression = "SET #s = :failed, failureReason = :reason, failedAtUtc = :now REMOVE ownerInstance",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":failed"] = new AttributeValue { S = "Failed" },
                            [":reason"] = new AttributeValue { S = reason ?? "error" },
                            [":now"] = new AttributeValue { N = nowTicks }
                        }
                    };

                    await _ddb.UpdateItemAsync(upd, ct).ConfigureAwait(false);
                }
                else
                {
                    // Requeue with small backoff by setting status back to Queued and clearing ownerInstance
                    var backoffSeconds = Math.Min(60, 1 << attempts); // exponential backoff capped at 60s
                    var nextVisibleAt = DateTimeOffset.UtcNow.AddSeconds(backoffSeconds).ToUnixTimeSeconds();

                    var upd = new UpdateItemRequest
                    {
                        TableName = _tableName,
                        Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = messageId } },
                        UpdateExpression = "SET #s = :queued, nextVisibleAt = :next REMOVE ownerInstance",
                        ExpressionAttributeNames = new Dictionary<string, string> { ["#s"] = "status" },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":queued"] = new AttributeValue { S = "Queued" },
                            [":next"] = new AttributeValue { N = nextVisibleAt.ToString() }
                        }
                    };

                    await _ddb.UpdateItemAsync(upd, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark message {MessageId} as Failed/Requeued", messageId);
            }
        }
    }

    /// <summary>
    /// Minimal remote forwarder contract used by DeliveryWorker.
    /// Implement the actual HTTP/gRPC forwarding in your RemoteForwarder class.
    /// </summary>
    //public interface IRemoteForwarder
    //{
    //    Task ForwardMessageAsync(string instanceAddress, string messageId, string targetType, string targetId, string payload, CancellationToken ct = default);
    //}
}
