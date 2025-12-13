// src/Infrastructure/Registry/DdbInstanceRegistry.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// DynamoDB-backed implementation of IInstanceRegistry.
    /// Assumes single table with PK = "id" and a "type" discriminator.
    /// Instance items: id = "instance:{instanceId}", type = "instance"
    /// Connection items: id = "conn:{connectionId}", type = "ws", ownerInstance = "{instanceId}"
    /// </summary>
    public sealed class DdbInstanceRegistry : IInstanceRegistry
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbInstanceRegistry>? _logger;

        private readonly IServiceProvider _svc;
        public DdbInstanceRegistry(IServiceProvider svc, ILogger<DdbInstanceRegistry>? logger = null)
        {
            _svc = svc;
            using var scope = svc.GetService<DynamodbService>();
            var ddb = scope?.DdbClient;
            var tbname = scope?.Options.TableName;

            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = tbname ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger;
        }

        private static string InstanceKey(string instanceId) => $"instance:{instanceId}";
        private static string ConnKey(string connectionId) => $"conn:{connectionId}";

        public async Task RegisterAsync(IInstanceRegistry.InstanceInfo instance, CancellationToken ct = default)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = InstanceKey(instance.InstanceId) },
                ["type"] = new AttributeValue { S = "instance" },
                ["address"] = new AttributeValue { S = instance.Address },
                ["lastHeartbeatUtc"] = new AttributeValue { N = instance.LastHeartbeatUtc.Ticks.ToString() }
            };

            if (instance.Metadata != null && instance.Metadata.Count > 0)
            {
                // store metadata as JSON string for simplicity
                var json = System.Text.Json.JsonSerializer.Serialize(instance.Metadata);
                item["metadata"] = new AttributeValue { S = json };
            }

            var req = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _ddb.PutItemAsync(req, ct).ConfigureAwait(false);
        }

        public async Task<bool> HeartbeatAsync(string instanceId, CancellationToken ct = default)
        {
            var key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = InstanceKey(instanceId) } };
            var nowTicks = DateTime.UtcNow.Ticks.ToString();

            var req = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = key,
                UpdateExpression = "SET #hb = :now",
                ConditionExpression = "attribute_exists(id) AND #type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#hb"] = "lastHeartbeatUtc",
                    ["#type"] = "type"
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":now"] = new AttributeValue { N = nowTicks },
                    [":typeVal"] = new AttributeValue { S = "instance" }
                },
                ReturnValues = "NONE"
            };

            try
            {
                await _ddb.UpdateItemAsync(req, ct).ConfigureAwait(false);
                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
        }

        public async Task<bool> DeregisterAsync(string instanceId, CancellationToken ct = default)
        {
            // Delete instance item
            var delReq = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = InstanceKey(instanceId) } },
                ConditionExpression = "#type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "instance" } }
            };

            try
            {
                await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
            }
            catch (ConditionalCheckFailedException)
            {
                // instance didn't exist
                return false;
            }

            // Release ownership of any connections owned by this instance
            // Simple scan for conn items with ownerInstance == instanceId and update them to remove ownerInstance.
            // Note: for large fleets consider a GSI on ownerInstance to avoid full scans.
            var filterExpr = "#type = :ws AND ownerInstance = :inst";
            var scanReq = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = filterExpr,
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ws"] = new AttributeValue { S = "ws" },
                    [":inst"] = new AttributeValue { S = instanceId }
                },
                ProjectionExpression = "id"
            };

            try
            {
                do
                {
                    var scanResp = await _ddb.ScanAsync(scanReq, ct).ConfigureAwait(false);
                    foreach (var item in scanResp.Items)
                    {
                        if (item.TryGetValue("id", out var idAttr) && idAttr.S != null)
                        {
                            var updateReq = new UpdateItemRequest
                            {
                                TableName = _tableName,
                                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = idAttr.S } },
                                UpdateExpression = "REMOVE ownerInstance",
                                ConditionExpression = "ownerInstance = :inst",
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":inst"] = new AttributeValue { S = instanceId } }
                            };

                            try
                            {
                                await _ddb.UpdateItemAsync(updateReq, ct).ConfigureAwait(false);
                            }
                            catch (ConditionalCheckFailedException) { /* ignore race */ }
                        }
                    }

                    scanReq.ExclusiveStartKey = scanResp.LastEvaluatedKey;
                } while (scanReq.ExclusiveStartKey != null && scanReq.ExclusiveStartKey.Count > 0);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error releasing connections for deregistered instance {InstanceId}", instanceId);
            }

            return true;
        }

        public async Task<IInstanceRegistry.InstanceInfo?> GetInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = InstanceKey(instanceId) } },
                ProjectionExpression = "id, address, lastHeartbeatUtc, metadata, type"
            };

            var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) return null;
            if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "instance") return null;

            resp.Item.TryGetValue("address", out var addr);
            resp.Item.TryGetValue("lastHeartbeatUtc", out var hb);
            resp.Item.TryGetValue("metadata", out var meta);

            var lastHeartbeat = hb != null && hb.N != null ? new DateTime(long.Parse(hb.N), DateTimeKind.Utc) : DateTime.MinValue;
            IReadOnlyDictionary<string, string>? metadata = null;
            if (meta != null && meta.S != null)
            {
                try
                {
                    metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(meta.S);
                }
                catch { /* ignore parse errors */ }
            }

            var id = instanceId;
            var address = addr?.S ?? string.Empty;
            return new IInstanceRegistry.InstanceInfo(id, address, lastHeartbeat, metadata);
        }

        public async Task<IReadOnlyList<IInstanceRegistry.InstanceInfo>> ListInstancesAsync(CancellationToken ct = default)
        {
            var scanReq = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "instance" } },
                ProjectionExpression = "id, address, lastHeartbeatUtc, metadata"
            };

            var results = new List<IInstanceRegistry.InstanceInfo>();
            do
            {
                var resp = await _ddb.ScanAsync(scanReq, ct).ConfigureAwait(false);
                foreach (var item in resp.Items)
                {
                    if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) continue;
                    var instanceId = idAttr.S.StartsWith("instance:") ? idAttr.S.Substring("instance:".Length) : idAttr.S;
                    item.TryGetValue("address", out var addr);
                    item.TryGetValue("lastHeartbeatUtc", out var hb);
                    item.TryGetValue("metadata", out var meta);

                    var lastHeartbeat = hb != null && hb.N != null ? new DateTime(long.Parse(hb.N), DateTimeKind.Utc) : DateTime.MinValue;
                    IReadOnlyDictionary<string, string>? metadata = null;
                    if (meta != null && meta.S != null)
                    {
                        try { metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(meta.S); } catch { }
                    }

                    results.Add(new IInstanceRegistry.InstanceInfo(instanceId, addr?.S ?? string.Empty, lastHeartbeat, metadata));
                }

                scanReq.ExclusiveStartKey = resp.LastEvaluatedKey;
            } while (scanReq.ExclusiveStartKey != null && scanReq.ExclusiveStartKey.Count > 0);

            return results;
        }

        public async Task<bool> TryClaimConnectionAsync(string connectionId, string instanceId, CancellationToken ct = default)
        {
            var key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } };
            var nowTicks = DateTime.UtcNow.Ticks.ToString();

            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = key,
                UpdateExpression = "SET ownerInstance = :inst, lastSeenUtc = :now",
                ConditionExpression = "attribute_not_exists(ownerInstance) OR ownerInstance = :inst",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":inst"] = new AttributeValue { S = instanceId },
                    [":now"] = new AttributeValue { N = nowTicks }
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

        public async Task<bool> ReleaseConnectionAsync(string connectionId, string instanceId, CancellationToken ct = default)
        {
            var key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } };

            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = key,
                UpdateExpression = "REMOVE ownerInstance",
                ConditionExpression = "ownerInstance = :inst",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":inst"] = new AttributeValue { S = instanceId } },
                ReturnValues = "NONE"
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

        public async Task<string?> GetOwnerForConnectionAsync(string connectionId, CancellationToken ct = default)
        {
            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                ProjectionExpression = "ownerInstance, type"
            };

            var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) return null;
            if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "ws") return null;
            if (resp.Item.TryGetValue("ownerInstance", out var owner) && owner.S != null) return owner.S;
            return null;
        }

        public async Task<IReadOnlyList<string>> CleanupStaleInstancesAsync(TimeSpan staleAfter, CancellationToken ct = default)
        {
            var cutoff = DateTime.UtcNow - staleAfter;
            var cutoffTicks = cutoff.Ticks.ToString();

            var removed = new List<string>();

            // Scan for instance items with lastHeartbeatUtc < cutoff
            var scanReq = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = "#type = :typeVal AND lastHeartbeatUtc < :cutoff",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":typeVal"] = new AttributeValue { S = "instance" },
                    [":cutoff"] = new AttributeValue { N = cutoffTicks }
                },
                ProjectionExpression = "id"
            };

            do
            {
                var resp = await _ddb.ScanAsync(scanReq, ct).ConfigureAwait(false);
                foreach (var item in resp.Items)
                {
                    if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) continue;
                    var instanceId = idAttr.S.StartsWith("instance:") ? idAttr.S.Substring("instance:".Length) : idAttr.S;

                    try
                    {
                        // attempt to delete instance (conditional to ensure it's still stale)
                        var delReq = new DeleteItemRequest
                        {
                            TableName = _tableName,
                            Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = idAttr.S } },
                            ConditionExpression = "attribute_exists(id) AND lastHeartbeatUtc < :cutoff",
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":cutoff"] = new AttributeValue { N = cutoffTicks } }
                        };

                        await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
                        removed.Add(instanceId);

                        // release connections owned by this instance (best-effort)
                        var scanConns = new ScanRequest
                        {
                            TableName = _tableName,
                            FilterExpression = "#type = :ws AND ownerInstance = :inst",
                            ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                            {
                                [":ws"] = new AttributeValue { S = "ws" },
                                [":inst"] = new AttributeValue { S = instanceId }
                            },
                            ProjectionExpression = "id"
                        };

                        do
                        {
                            var connsResp = await _ddb.ScanAsync(scanConns, ct).ConfigureAwait(false);
                            foreach (var connItem in connsResp.Items)
                            {
                                if (connItem.TryGetValue("id", out var connIdAttr) && connIdAttr.S != null)
                                {
                                    var upd = new UpdateItemRequest
                                    {
                                        TableName = _tableName,
                                        Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = connIdAttr.S } },
                                        UpdateExpression = "REMOVE ownerInstance",
                                        ConditionExpression = "ownerInstance = :inst",
                                        ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":inst"] = new AttributeValue { S = instanceId } }
                                    };

                                    try { await _ddb.UpdateItemAsync(upd, ct).ConfigureAwait(false); } catch (ConditionalCheckFailedException) { }
                                }
                            }

                            scanConns.ExclusiveStartKey = connsResp.LastEvaluatedKey;
                        } while (scanConns.ExclusiveStartKey != null && scanConns.ExclusiveStartKey.Count > 0);
                    }
                    catch (ConditionalCheckFailedException) { /* someone updated it concurrently */ }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Error cleaning up stale instance {InstanceId}", instanceId);
                    }
                }

                scanReq.ExclusiveStartKey = resp.LastEvaluatedKey;
            } while (scanReq.ExclusiveStartKey != null && scanReq.ExclusiveStartKey.Count > 0);

            return removed;
        }
    }
}
