// src/Infrastructure/Storage/Dynamo/SetEntity.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Representation of a set (collection of string members) stored in the shared single-table DynamoDB.
    /// Conventions:
    ///  - PK: id (string) — typically "set:{name}" or a GUID
    ///  - type: discriminator (e.g., "set")
    ///  - key: logical set name (string)
    ///  - members: string set (SS)
    ///  - createdAtUtc: ticks (number)
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    ///  - version: optional optimistic concurrency token (number)
    /// </summary>
    public sealed class SetEntity
    {
        public const string TypeDiscriminator = "set";

        public string Id { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;

        // Backing storage for EF mapping: JSON column containing members.
        // EF configuration can map this to a nvarchar(max) column named "MembersJson".
        // Keep it public with getter/setter so EF can read/write it.
        public string? MembersJson
        {
            get => _membersJson;
            set
            {
                _membersJson = value;
                // update the cached members view
                _members = (ICollection<string>)ParseMembersJson(value);
            }
        }
        private string? _membersJson;

        // Strongly-typed view of members. Not mapped by EF directly (EF maps MembersJson).
        private ICollection<string> _members = Array.Empty<string>();
        public ICollection<string> Members
        {
            get => _members;
            init
            {
                _members = (value ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
                _membersJson = SerializeMembers(_members);
            }
        }

        // CreatedAtUtcTicks preserved for DynamoDB semantics
        public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;

        // EF mapping expects UpdatedAtUtc; provide a DateTime property that the Db configuration can index.
        // Keep it settable so EF can update it.
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public long? TtlUnixSeconds { get; init; }
        public long? Version { get; init; }

        public SetEntity() { }

        /// <summary>
        /// Primary constructor.
        /// </summary>
        /// <param name="id">Entity id (e.g., set:{key}).</param>
        /// <param name="key">Logical set key.</param>
        /// <param name="members">Optional members collection.</param>
        /// <param name="ttlUnixSeconds">Optional TTL (unix seconds).</param>
        /// <param name="version">Optional version token.</param>
        /// <param name="createdAtUtcTicks">Optional created ticks to preserve original creation time when copying.</param>
        public SetEntity(string id, string key, IEnumerable<string>? members = null, long? ttlUnixSeconds = null, long? version = null, long? createdAtUtcTicks = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Key = key ?? throw new ArgumentNullException(nameof(key));

            var normalized = (members ?? Array.Empty<string>()).Where(m => m != null).Select(m => m!).Distinct(StringComparer.Ordinal).ToArray();
            _members = normalized;
            _membersJson = SerializeMembers(normalized);

            CreatedAtUtcTicks = createdAtUtcTicks ?? DateTime.UtcNow.Ticks;
            UpdatedAtUtc = DateTime.UtcNow;
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
                ["key"] = new AttributeValue { S = Key },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() }
            };

            if (Members != null && Members.Count > 0)
            {
                // Use string set (SS) for members
                item["members"] = new AttributeValue { SS = Members.ToList() };
            }

            if (TtlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = TtlUnixSeconds.Value.ToString() };

            if (Version.HasValue)
                item["version"] = new AttributeValue { N = Version.Value.ToString() };

            return item;
        }

        /// <summary>
        /// Try to create a SetEntity from a DynamoDB item. Returns null if the item is not a set entity.
        /// </summary>
        public static SetEntity? FromItem(IDictionary<string, AttributeValue>? item)
        {
            if (item == null || item.Count == 0) return null;

            if (!item.TryGetValue("type", out var typeAttr) || typeAttr.S != TypeDiscriminator) return null;
            if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) return null;

            var id = idAttr.S!;
            item.TryGetValue("key", out var keyAttr);
            item.TryGetValue("members", out var membersAttr);
            item.TryGetValue("createdAtUtc", out var createdAttr);
            item.TryGetValue("ttl", out var ttlAttr);
            item.TryGetValue("version", out var verAttr);

            var key = keyAttr?.S ?? string.Empty;

            // Members: prefer SS (string set), but also accept L of strings for compatibility
            var members = new List<string>();
            if (membersAttr != null)
            {
                if (membersAttr.SS != null && membersAttr.SS.Count > 0)
                {
                    members.AddRange(membersAttr.SS);
                }
                else if (membersAttr.L != null && membersAttr.L.Count > 0)
                {
                    foreach (var av in membersAttr.L)
                    {
                        if (av.S != null) members.Add(av.S);
                    }
                }
            }

            long createdTicks = DateTime.UtcNow.Ticks;
            if (createdAttr?.N != null && long.TryParse(createdAttr.N, out var parsedCreated))
                createdTicks = parsedCreated;

            long? ttl = null;
            if (ttlAttr?.N != null && long.TryParse(ttlAttr.N, out var parsedTtl))
                ttl = parsedTtl;

            long? version = null;
            if (verAttr?.N != null && long.TryParse(verAttr.N, out var parsedVer))
                version = parsedVer;

            var entity = new SetEntity(id, key, members, ttl, version, createdAtUtcTicks: createdTicks)
            {
                // Keep UpdatedAtUtc in sync with CreatedAtUtc by default; callers can update later.
                UpdatedAtUtc = new DateTime(createdTicks, DateTimeKind.Utc)
            };

            return entity;
        }

        /// <summary>
        /// Helper to build a canonical id for a logical set name.
        /// </summary>
        public static string MakeIdForKey(string key) => $"set:{key}";

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// Return a new SetEntity with the given member added (idempotent).
        /// </summary>
        public SetEntity WithMemberAdded(string member)
        {
            if (string.IsNullOrEmpty(member)) return this;
            var newMembers = Members.Concat(new[] { member }).Distinct(StringComparer.Ordinal).ToArray();

            // Preserve CreatedAtUtcTicks, TtlUnixSeconds and Version by constructing a new instance
            return new SetEntity(
                id: this.Id,
                key: this.Key,
                members: newMembers,
                ttlUnixSeconds: this.TtlUnixSeconds,
                version: this.Version,
                createdAtUtcTicks: this.CreatedAtUtcTicks)
            {
                UpdatedAtUtc = DateTime.UtcNow
            };
        }





        /// <summary>
        /// Return a new SetEntity with the given member removed.
        /// </summary>
        public SetEntity WithMemberRemoved(string member)
        {
            if (string.IsNullOrEmpty(member)) return this;
            var newMembers = Members.Where(m => !string.Equals(m, member, StringComparison.Ordinal)).ToArray();

            return new SetEntity(
                id: this.Id,
                key: this.Key,
                members: newMembers,
                ttlUnixSeconds: this.TtlUnixSeconds,
                version: this.Version,
                createdAtUtcTicks: this.CreatedAtUtcTicks)
            {
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        // Helpers for JSON serialization of members (used by MembersJson)
        private static string SerializeMembers(IEnumerable<string> members)
        {
            // Use System.Text.Json for compact representation
            return JsonSerializer.Serialize(members.Distinct(StringComparer.Ordinal));
        }

        private static ICollection<string> ParseMembersJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
            try
            {
                var parsed = JsonSerializer.Deserialize<string[]>(json);
                return (parsed ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            }
            catch
            {
                // On parse error, return empty set to avoid throwing in EF materialization
                return Array.Empty<string>();
            }
        }
    }
}
