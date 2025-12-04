// src/Infrastructure/Connections/DdbBackedConnectionManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Connection manager backed by a single DynamoDB table (PK = "id", "type" discriminator).
    /// Connection item shape:
    ///   id = "conn:{connectionId}"
    ///   type = "ws"
    ///   userId = "<optional>"
    ///   rooms = "<json array>" (string)
    ///   ownerInstance = "<instanceId>" (optional)
    ///   createdAtUtc = <ticks>
    ///   lastSeenUtc = <ticks>
    /// TTL may be set externally if desired.
    /// </summary>
    public sealed class DdbBackedConnectionManager : IDisposable
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbBackedConnectionManager>? _logger;
        private bool _disposed;

        private readonly IServiceProvider _svc;
        public DdbBackedConnectionManager(IServiceProvider svc, ILogger<DdbBackedConnectionManager>? logger = null)
        {
            _svc = svc;
            using var scope = svc.GetService<DynamodbService>();
            var ddb = scope?.DdbClient;
            var tbname = scope?.Options.TableName;

            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = tbname ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger;
        }

        private static string ConnKey(string connectionId) => $"conn:{connectionId}";

        /// <summary>
        /// Create or upsert a connection item. If it already exists, updates lastSeenUtc and ownerInstance if provided.
        /// </summary>
        public async Task CreateOrUpdateConnectionAsync(string connectionId, string? userId = null, string? ownerInstance = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            var nowTicks = DateTime.UtcNow.Ticks.ToString();
            var item = new Dictionary<string, AttributeValue>
            {
                ["id"] = new AttributeValue { S = ConnKey(connectionId) },
                ["type"] = new AttributeValue { S = "ws" },
                ["createdAtUtc"] = new AttributeValue { N = nowTicks },
                ["lastSeenUtc"] = new AttributeValue { N = nowTicks }
            };

            if (!string.IsNullOrEmpty(userId)) item["userId"] = new AttributeValue { S = userId };
            if (!string.IsNullOrEmpty(ownerInstance)) item["ownerInstance"] = new AttributeValue { S = ownerInstance };
            item["rooms"] = new AttributeValue { S = "[]" }; // initialize empty rooms if new

            var putReq = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a connection item.
        /// </summary>
        public async Task<bool> RemoveConnectionAsync(string connectionId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            var delReq = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                ConditionExpression = "#type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "ws" } }
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
        }

        /// <summary>
        /// Get connection details. Returns null if not found or wrong type.
        /// </summary>
        public async Task<ConnectionInfo?> GetConnectionAsync(string connectionId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                ProjectionExpression = "id, type, userId, rooms, ownerInstance, createdAtUtc, lastSeenUtc"
            };

            var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) return null;
            if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "ws") return null;

            resp.Item.TryGetValue("userId", out var userAttr);
            resp.Item.TryGetValue("rooms", out var roomsAttr);
            resp.Item.TryGetValue("ownerInstance", out var ownerAttr);
            resp.Item.TryGetValue("createdAtUtc", out var createdAttr);
            resp.Item.TryGetValue("lastSeenUtc", out var lastSeenAttr);

            string[] rooms = Array.Empty<string>();
            if (roomsAttr?.S != null)
            {
                try { rooms = JsonSerializer.Deserialize<string[]>(roomsAttr.S) ?? Array.Empty<string>(); } catch { rooms = Array.Empty<string>(); }
            }

            var created = createdAttr?.N != null ? new DateTime(long.Parse(createdAttr.N), DateTimeKind.Utc) : DateTime.MinValue;
            var lastSeen = lastSeenAttr?.N != null ? new DateTime(long.Parse(lastSeenAttr.N), DateTimeKind.Utc) : DateTime.MinValue;

            return new ConnectionInfo(connectionId, userAttr?.S, rooms, ownerAttr?.S, created, lastSeen);
        }

        /// <summary>
        /// Add a connection to a room (idempotent). Uses conditional update to avoid races.
        /// </summary>
        public async Task AddConnectionToRoomAsync(string connectionId, string room, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));

            // Read-modify-write: get current rooms, add if missing, update
            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                ProjectionExpression = "rooms, type"
            };

            var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) throw new InvalidOperationException("Connection not found");
            if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "ws") throw new InvalidOperationException("Invalid item type");

            var roomsJson = resp.Item.TryGetValue("rooms", out var r) && r.S != null ? r.S : "[]";
            string[] roomsArr;
            try { roomsArr = JsonSerializer.Deserialize<string[]>(roomsJson) ?? Array.Empty<string>(); } catch { roomsArr = Array.Empty<string>(); }

            if (roomsArr.Contains(room, StringComparer.Ordinal)) return; // already a member

            var newRooms = roomsArr.Concat(new[] { room }).ToArray();
            var newRoomsJson = JsonSerializer.Serialize(newRooms);

            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                UpdateExpression = "SET rooms = :rooms, lastSeenUtc = :now",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":rooms"] = new AttributeValue { S = newRoomsJson },
                    [":now"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
                }
            };

            await _ddb.UpdateItemAsync(updateReq, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Remove a connection from a room (idempotent).
        /// </summary>
        public async Task RemoveConnectionFromRoomAsync(string connectionId, string room, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));

            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                ProjectionExpression = "rooms, type"
            };

            var resp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) return;
            if (!resp.Item.TryGetValue("type", out var typeAttr) || typeAttr.S != "ws") return;

            var roomsJson = resp.Item.TryGetValue("rooms", out var r) && r.S != null ? r.S : "[]";
            string[] roomsArr;
            try { roomsArr = JsonSerializer.Deserialize<string[]>(roomsJson) ?? Array.Empty<string>(); } catch { roomsArr = Array.Empty<string>(); }

            if (!roomsArr.Contains(room, StringComparer.Ordinal)) return; // nothing to do

            var newRooms = roomsArr.Where(x => !string.Equals(x, room, StringComparison.Ordinal)).ToArray();
            var newRoomsJson = JsonSerializer.Serialize(newRooms);

            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                UpdateExpression = "SET rooms = :rooms, lastSeenUtc = :now",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":rooms"] = new AttributeValue { S = newRoomsJson },
                    [":now"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
                }
            };

            await _ddb.UpdateItemAsync(updateReq, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// List connection ids that are members of a room.
        /// Note: without a GSI this performs a scan filtered by type and rooms contains; acceptable for small scale or testing.
        /// For production, add a GSI on room membership or maintain reverse mapping items.
        /// </summary>
        public async Task<string[]> ListConnectionsInRoomAsync(string room, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));

            // Scan and filter by type = "ws" and rooms contains the room string.
            // Because rooms is stored as JSON string, we do a contains filter on the JSON text.
            var filterExpr = "#type = :ws AND contains(rooms, :roomJson)";
            var scanReq = new ScanRequest
            {
                TableName = _tableName,
                FilterExpression = filterExpr,
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ws"] = new AttributeValue { S = "ws" },
                    [":roomJson"] = new AttributeValue { S = $"\"{room}\"" } // match JSON string element
                },
                ProjectionExpression = "id"
            };

            var results = new List<string>();
            do
            {
                var resp = await _ddb.ScanAsync(scanReq, ct).ConfigureAwait(false);
                foreach (var item in resp.Items)
                {
                    if (item.TryGetValue("id", out var idAttr) && idAttr.S != null)
                    {
                        var connId = idAttr.S.StartsWith("conn:") ? idAttr.S.Substring("conn:".Length) : idAttr.S;
                        results.Add(connId);
                    }
                }

                scanReq.ExclusiveStartKey = resp.LastEvaluatedKey;
            } while (scanReq.ExclusiveStartKey != null && scanReq.ExclusiveStartKey.Count > 0);

            return results.ToArray();
        }

        /// <summary>
        /// Update lastSeenUtc for a connection (heartbeat).
        /// </summary>
        public async Task<bool> UpdateLastSeenAsync(string connectionId, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            var updateReq = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                UpdateExpression = "SET lastSeenUtc = :now",
                ConditionExpression = "#type = :typeVal",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":now"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }, [":typeVal"] = new AttributeValue { S = "ws" } }
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

        /// <summary>
        /// Set or change ownerInstance for a connection (atomic).
        /// </summary>
        public async Task<bool> SetOwnerInstanceAsync(string connectionId, string? ownerInstance, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            if (string.IsNullOrEmpty(ownerInstance))
            {
                // remove ownerInstance
                var updateReq = new UpdateItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                    UpdateExpression = "REMOVE ownerInstance",
                    ConditionExpression = "#type = :typeVal",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":typeVal"] = new AttributeValue { S = "ws" } }
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
            else
            {
                var updateReq = new UpdateItemRequest
                {
                    TableName = _tableName,
                    Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = ConnKey(connectionId) } },
                    UpdateExpression = "SET ownerInstance = :inst, lastSeenUtc = :now",
                    ConditionExpression = "#type = :typeVal",
                    ExpressionAttributeNames = new Dictionary<string, string> { ["#type"] = "type" },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":inst"] = new AttributeValue { S = ownerInstance },
                        [":now"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() },
                        [":typeVal"] = new AttributeValue { S = "ws" }
                    }
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
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdbBackedConnectionManager));
        }

        public void Dispose()
        {
            _disposed = true;
        }

        /// <summary>
        /// Lightweight DTO returned by GetConnectionAsync.
        /// </summary>
        public sealed record ConnectionInfo(
            string ConnectionId,
            string? UserId,
            string[] Rooms,
            string? OwnerInstance,
            DateTime CreatedAtUtc,
            DateTime LastSeenUtc);
    }
}
