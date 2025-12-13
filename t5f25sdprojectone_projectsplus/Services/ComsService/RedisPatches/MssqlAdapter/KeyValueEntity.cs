// src/Infrastructure/Storage/Dynamo/KeyValueEntity.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Simple key/value entity representation stored in the shared single-table DynamoDB.
    /// Designed to be small and easy to convert to/from DynamoDB AttributeValue dictionaries.
    /// 
    /// Table conventions used by helpers in this class:
    ///  - PK: id (string) — typically "kv:{key}" or a GUID
    ///  - type: discriminator (e.g., "kv")
    ///  - key: logical key (string)
    ///  - value: stored payload (string)
    ///  - createdAtUtc: ticks (number)
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    ///  - version: optional optimistic concurrency token (number)
    /// </summary>
    public sealed class KeyValueEntity
    {
        public const string TypeDiscriminator = "kv";

        public string Id { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public long? TtlUnixSeconds { get; init; }
        public long? Version { get; init; }

        public KeyValueEntity() { }

        public KeyValueEntity(string id, string key, string value, long? ttlUnixSeconds = null, long? version = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Value = value ?? string.Empty;
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
            TtlUnixSeconds = ttlUnixSeconds;
            Version = version;
        }

        public long UpdatedAtUtcTicks { get; set; } = DateTime.UtcNow.Ticks;

        public DateTime UpdatedAtUtc
        {
            get => new(UpdatedAtUtcTicks, DateTimeKind.Utc);
            set => UpdatedAtUtcTicks = value.Ticks;
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
                ["value"] = new AttributeValue { S = Value },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() }
            };

            if (TtlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = TtlUnixSeconds.Value.ToString() };

            if (Version.HasValue)
                item["version"] = new AttributeValue { N = Version.Value.ToString() };

            return item;
        }

        /// <summary>
        /// Try to create a KeyValueEntity from a DynamoDB item. Returns null if the item is not a kv entity.
        /// </summary>
        public static KeyValueEntity? FromItem(IDictionary<string, AttributeValue>? item)
        {
            if (item == null || item.Count == 0) return null;

            if (!item.TryGetValue("type", out var typeAttr) || typeAttr.S != TypeDiscriminator) return null;
            if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) return null;

            var id = idAttr.S!;
            item.TryGetValue("key", out var keyAttr);
            item.TryGetValue("value", out var valueAttr);
            item.TryGetValue("createdAtUtc", out var createdAttr);
            item.TryGetValue("ttl", out var ttlAttr);
            item.TryGetValue("version", out var verAttr);

            var key = keyAttr?.S ?? string.Empty;
            var value = valueAttr?.S ?? string.Empty;

            long createdTicks = DateTime.UtcNow.Ticks;
            if (createdAttr?.N != null && long.TryParse(createdAttr.N, out var parsedCreated))
                createdTicks = parsedCreated;

            long? ttl = null;
            if (ttlAttr?.N != null && long.TryParse(ttlAttr.N, out var parsedTtl))
                ttl = parsedTtl;

            long? version = null;
            if (verAttr?.N != null && long.TryParse(verAttr.N, out var parsedVer))
                version = parsedVer;

            return new KeyValueEntity
            {
                Id = id,
                Key = key,
                Value = value,
                CreatedAtUtcTicks = createdTicks,
                TtlUnixSeconds = ttl,
                Version = version
            };
        }

        /// <summary>
        /// Helper to build a canonical id for a logical key.
        /// </summary>
        public static string MakeIdForKey(string key) => $"kv:{key}";

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);
    }
}
