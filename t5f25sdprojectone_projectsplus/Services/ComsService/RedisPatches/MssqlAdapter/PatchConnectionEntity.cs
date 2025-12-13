// src/Infrastructure/Storage/Dynamo/PatchConnectionEntity.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Representation of a connection (websocket / client) stored in the shared single-table DynamoDB.
    /// Conventions:
    ///  - PK: id (string) — typically "conn:{connectionId}" or a GUID
    ///  - type: discriminator (e.g., "connection")
    ///  - connectionId: unique connection identifier (string)
    ///  - userId: associated user id (string)
    ///  - ownerInstance: owning instance id (string) — used for claims/forwarding
    ///  - rooms: string set (SS) or list (L) of room ids
    ///  - createdAtUtc: ticks (number)
    ///  - lastHeartbeatUtc: ticks (number) — optional
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    ///  - version: optional optimistic concurrency token (number)
    /// </summary>
    public sealed class PatchConnectionEntity
    {
        public const string TypeDiscriminator = "connection";

        public string Id { get; init; } = string.Empty;
        public string ConnectionId { get; init; } = string.Empty;
        public string? UserId { get; init; }
        public string? OwnerInstance { get; init; }
        public ICollection<string> Rooms { get; init; } = Array.Empty<string>();
        public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public long? LastHeartbeatUtcTicks { get; init; }
        public long? TtlUnixSeconds { get; init; }
        public long? Version { get; init; }

        public PatchConnectionEntity() { }

        public PatchConnectionEntity(string id, string connectionId, string? userId = null, string? ownerInstance = null, IEnumerable<string>? rooms = null, long? ttlUnixSeconds = null, long? version = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ConnectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
            UserId = userId;
            OwnerInstance = ownerInstance;
            Rooms = (rooms ?? Array.Empty<string>()).Where(r => r != null).Select(r => r!).Distinct(StringComparer.Ordinal).ToArray();
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
            TtlUnixSeconds = ttlUnixSeconds;
            Version = version;
        }

        /// <summary>
        /// Convert this entity into a DynamoDB item dictionary.
        /// </summary>
        public Dictionary<string, AttributeValue> ToItem()
        {
            var item = new Dictionary<string, AttributeValue>(StringComparer.Ordinal)
            {
                ["id"] = new AttributeValue { S = Id },
                ["type"] = new AttributeValue { S = TypeDiscriminator },
                ["connectionId"] = new AttributeValue { S = ConnectionId },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() }
            };

            if (!string.IsNullOrWhiteSpace(UserId))
                item["userId"] = new AttributeValue { S = UserId };

            if (!string.IsNullOrWhiteSpace(OwnerInstance))
                item["ownerInstance"] = new AttributeValue { S = OwnerInstance };

            if (Rooms != null && Rooms.Count > 0)
                item["rooms"] = new AttributeValue { SS = Rooms.ToList() };

            if (LastHeartbeatUtcTicks.HasValue)
                item["lastHeartbeatUtc"] = new AttributeValue { N = LastHeartbeatUtcTicks.Value.ToString() };

            if (TtlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = TtlUnixSeconds.Value.ToString() };

            if (Version.HasValue)
                item["version"] = new AttributeValue { N = Version.Value.ToString() };

            return item;
        }

        /// <summary>
        /// Try to create a PatchConnectionEntity from a DynamoDB item. Returns null if the item is not a connection entity.
        /// </summary>
        public static PatchConnectionEntity? FromItem(IDictionary<string, AttributeValue>? item)
        {
            if (item == null || item.Count == 0) return null;

            if (!item.TryGetValue("type", out var typeAttr) || typeAttr.S != TypeDiscriminator) return null;
            if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) return null;
            if (!item.TryGetValue("connectionId", out var connIdAttr) || connIdAttr.S == null) return null;

            var id = idAttr.S!;
            var connectionId = connIdAttr.S!;

            item.TryGetValue("userId", out var userIdAttr);
            item.TryGetValue("ownerInstance", out var ownerAttr);
            item.TryGetValue("rooms", out var roomsAttr);
            item.TryGetValue("createdAtUtc", out var createdAttr);
            item.TryGetValue("lastHeartbeatUtc", out var hbAttr);
            item.TryGetValue("ttl", out var ttlAttr);
            item.TryGetValue("version", out var verAttr);

            var userId = userIdAttr?.S;
            var ownerInstance = ownerAttr?.S;

            var rooms = new List<string>();
            if (roomsAttr != null)
            {
                if (roomsAttr.SS != null && roomsAttr.SS.Count > 0)
                {
                    rooms.AddRange(roomsAttr.SS);
                }
                else if (roomsAttr.L != null && roomsAttr.L.Count > 0)
                {
                    foreach (var av in roomsAttr.L)
                    {
                        if (av.S != null) rooms.Add(av.S);
                    }
                }
            }

            long createdTicks = DateTime.UtcNow.Ticks;
            if (createdAttr?.N != null && long.TryParse(createdAttr.N, out var parsedCreated))
                createdTicks = parsedCreated;

            long? lastHeartbeat = null;
            if (hbAttr?.N != null && long.TryParse(hbAttr.N, out var parsedHb))
                lastHeartbeat = parsedHb;

            long? ttl = null;
            if (ttlAttr?.N != null && long.TryParse(ttlAttr.N, out var parsedTtl))
                ttl = parsedTtl;

            long? version = null;
            if (verAttr?.N != null && long.TryParse(verAttr.N, out var parsedVer))
                version = parsedVer;

            return new PatchConnectionEntity
            {
                Id = id,
                ConnectionId = connectionId,
                UserId = userId,
                OwnerInstance = ownerInstance,
                Rooms = rooms.Distinct(StringComparer.Ordinal).ToArray(),
                CreatedAtUtcTicks = createdTicks,
                LastHeartbeatUtcTicks = lastHeartbeat,
                TtlUnixSeconds = ttl,
                Version = version
            };
        }

        /// <summary>
        /// Build a canonical id for a connection. Example: "conn:{connectionId}".
        /// </summary>
        public static string MakeId(string connectionId) => $"conn:{connectionId}";

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// Returns the LastHeartbeatUtc as a DateTime (UTC) or null if not set.
        /// </summary>
        public DateTime? LastHeartbeatUtc => LastHeartbeatUtcTicks.HasValue ? new DateTime(LastHeartbeatUtcTicks.Value, DateTimeKind.Utc) : null;

        /// <summary>
        /// Returns true if the connection is considered stale given the provided threshold.
        /// </summary>
        public bool IsStale(TimeSpan staleThreshold)
        {
            if (!LastHeartbeatUtcTicks.HasValue) return true;
            var age = DateTime.UtcNow.Ticks - LastHeartbeatUtcTicks.Value;
            return TimeSpan.FromTicks(age) > staleThreshold;
        }

        /// <summary>
        /// Return a copy with heartbeat updated to now.
        /// </summary>
        public PatchConnectionEntity WithHeartbeat()
        {
            return new PatchConnectionEntity
            {
                Id = Id,
                ConnectionId = ConnectionId,
                UserId = UserId,
                OwnerInstance = OwnerInstance,
                Rooms = Rooms,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                LastHeartbeatUtcTicks = DateTime.UtcNow.Ticks,
                TtlUnixSeconds = TtlUnixSeconds,
                Version = Version
            };
        }

        /// <summary>
        /// Return a copy with the given room added (idempotent).
        /// </summary>
        public PatchConnectionEntity WithRoomAdded(string room)
        {
            if (string.IsNullOrEmpty(room)) return this;
            var newRooms = Rooms.Concat(new[] { room }).Distinct(StringComparer.Ordinal).ToArray();
            return new PatchConnectionEntity
            {
                Id = Id,
                ConnectionId = ConnectionId,
                UserId = UserId,
                OwnerInstance = OwnerInstance,
                Rooms = newRooms,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                LastHeartbeatUtcTicks = LastHeartbeatUtcTicks,
                TtlUnixSeconds = TtlUnixSeconds,
                Version = Version
            };
        }

        /// <summary>
        /// Return a copy with the given room removed.
        /// </summary>
        public PatchConnectionEntity WithRoomRemoved(string room)
        {
            if (string.IsNullOrEmpty(room)) return this;
            var newRooms = Rooms.Where(r => !string.Equals(r, room, StringComparison.Ordinal)).ToArray();
            return new PatchConnectionEntity
            {
                Id = Id,
                ConnectionId = ConnectionId,
                UserId = UserId,
                OwnerInstance = OwnerInstance,
                Rooms = newRooms,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                LastHeartbeatUtcTicks = LastHeartbeatUtcTicks,
                TtlUnixSeconds = TtlUnixSeconds,
                Version = Version
            };
        }

        /// <summary>
        /// Return a copy with owner instance set (claim).
        /// </summary>
        public PatchConnectionEntity WithOwner(string? ownerInstance)
        {
            return new PatchConnectionEntity
            {
                Id = Id,
                ConnectionId = ConnectionId,
                UserId = UserId,
                OwnerInstance = ownerInstance,
                Rooms = Rooms,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                LastHeartbeatUtcTicks = LastHeartbeatUtcTicks,
                TtlUnixSeconds = TtlUnixSeconds,
                Version = Version
            };
        }
    }
}
