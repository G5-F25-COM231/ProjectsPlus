// src/Infrastructure/Storage/Dynamo/PatchDeadLetterEntity.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Representation of a dead-lettered message stored in the shared single-table DynamoDB.
    /// Conventions:
    ///  - PK: id (string) — typically "dl:{messageId}" or "dl:{channel}:{messageId}"
    ///  - type: discriminator (e.g., "deadletter")
    ///  - originalMessageId: id of the original message (string)
    ///  - channel: logical channel or topic (string)
    ///  - payload: original message body (string)
    ///  - reason: textual reason for dead-lettering (string)
    ///  - failedAtUtc: ticks (number)
    ///  - attempts: delivery attempt counter (number)
    ///  - createdAtUtc: ticks (number)
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    ///  - metadata: optional map of additional info (stored as JSON string in `metadata`)
    /// </summary>
    public sealed class PatchDeadLetterEntity
    {
        public const string TypeDiscriminator = "deadletter";

        public string Id { get; init; } = string.Empty;
        public string OriginalMessageId { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public string? Reason { get; init; }
        public long FailedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public int Attempts { get; init; } = 0;
        public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public long? TtlUnixSeconds { get; init; }
        public string? MetadataJson { get; init; }

        public PatchDeadLetterEntity() { }

        public PatchDeadLetterEntity(
            string id,
            string originalMessageId,
            string channel,
            string payload,
            string? reason = null,
            int attempts = 0,
            long? ttlUnixSeconds = null,
            string? metadataJson = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            OriginalMessageId = originalMessageId ?? throw new ArgumentNullException(nameof(originalMessageId));
            Channel = channel ?? string.Empty;
            Payload = payload ?? string.Empty;
            Reason = reason;
            Attempts = attempts;
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
            FailedAtUtcTicks = DateTime.UtcNow.Ticks;
            TtlUnixSeconds = ttlUnixSeconds;
            MetadataJson = metadataJson;
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
                ["originalMessageId"] = new AttributeValue { S = OriginalMessageId },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() },
                ["failedAtUtc"] = new AttributeValue { N = FailedAtUtcTicks.ToString() },
                ["attempts"] = new AttributeValue { N = Attempts.ToString() },
                ["payload"] = new AttributeValue { S = Payload }
            };

            if (!string.IsNullOrWhiteSpace(Channel))
                item["channel"] = new AttributeValue { S = Channel };

            if (!string.IsNullOrWhiteSpace(Reason))
                item["reason"] = new AttributeValue { S = Reason };

            if (TtlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = TtlUnixSeconds.Value.ToString() };

            if (!string.IsNullOrWhiteSpace(MetadataJson))
                item["metadata"] = new AttributeValue { S = MetadataJson };

            return item;
        }

        /// <summary>
        /// Try to create a PatchDeadLetterEntity from a DynamoDB item. Returns null if the item is not a dead-letter entity.
        /// </summary>
        public static PatchDeadLetterEntity? FromItem(IDictionary<string, AttributeValue>? item)
        {
            if (item == null || item.Count == 0) return null;

            if (!item.TryGetValue("type", out var typeAttr) || typeAttr.S != TypeDiscriminator) return null;
            if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) return null;
            if (!item.TryGetValue("originalMessageId", out var origAttr) || origAttr.S == null) return null;

            var id = idAttr.S!;
            var originalMessageId = origAttr.S!;

            item.TryGetValue("channel", out var channelAttr);
            item.TryGetValue("payload", out var payloadAttr);
            item.TryGetValue("reason", out var reasonAttr);
            item.TryGetValue("failedAtUtc", out var failedAttr);
            item.TryGetValue("attempts", out var attemptsAttr);
            item.TryGetValue("createdAtUtc", out var createdAttr);
            item.TryGetValue("ttl", out var ttlAttr);
            item.TryGetValue("metadata", out var metadataAttr);

            var channel = channelAttr?.S ?? string.Empty;
            var payload = payloadAttr?.S ?? string.Empty;
            var reason = reasonAttr?.S;
            var metadata = metadataAttr?.S;

            long createdTicks = DateTime.UtcNow.Ticks;
            if (createdAttr?.N != null && long.TryParse(createdAttr.N, out var parsedCreated))
                createdTicks = parsedCreated;

            long failedTicks = DateTime.UtcNow.Ticks;
            if (failedAttr?.N != null && long.TryParse(failedAttr.N, out var parsedFailed))
                failedTicks = parsedFailed;

            int attempts = 0;
            if (attemptsAttr?.N != null && int.TryParse(attemptsAttr.N, out var parsedAttempts))
                attempts = parsedAttempts;

            long? ttl = null;
            if (ttlAttr?.N != null && long.TryParse(ttlAttr.N, out var parsedTtl))
                ttl = parsedTtl;

            return new PatchDeadLetterEntity
            {
                Id = id,
                OriginalMessageId = originalMessageId,
                Channel = channel,
                Payload = payload,
                Reason = reason,
                FailedAtUtcTicks = failedTicks,
                Attempts = attempts,
                CreatedAtUtcTicks = createdTicks,
                TtlUnixSeconds = ttl,
                MetadataJson = metadata
            };
        }

        /// <summary>
        /// Build a canonical id for a dead-letter entry. Example: "dl:{channel}:{messageId}" or "dl:{messageId}" if channel is empty.
        /// </summary>
        public static string MakeId(string channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channel)) return $"dl:{messageId}";
            return $"dl:{channel}:{messageId}";
        }

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// Returns the FailedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime FailedAtUtc => new DateTime(FailedAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// Returns true if the dead-letter entry is expired according to ttl (if present).
        /// </summary>
        public bool IsExpired()
        {
            if (!TtlUnixSeconds.HasValue) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return TtlUnixSeconds.Value < now;
        }

        /// <summary>
        /// Return a copy with attempts incremented and optional reason/failed timestamp updated.
        /// </summary>
        public PatchDeadLetterEntity WithAttemptIncremented(string? reason = null, bool updateFailedAt = true)
        {
            var failed = FailedAtUtcTicks;
            if (updateFailedAt) failed = DateTime.UtcNow.Ticks;

            return new PatchDeadLetterEntity
            {
                Id = Id,
                OriginalMessageId = OriginalMessageId,
                Channel = Channel,
                Payload = Payload,
                Reason = reason ?? Reason,
                FailedAtUtcTicks = failed,
                Attempts = Attempts + 1,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                TtlUnixSeconds = TtlUnixSeconds,
                MetadataJson = MetadataJson
            };
        }

        /// <summary>
        /// Return a copy with TTL set (unix epoch seconds).
        /// </summary>
        public PatchDeadLetterEntity WithTtl(long ttlUnixSeconds)
        {
            return new PatchDeadLetterEntity
            {
                Id = Id,
                OriginalMessageId = OriginalMessageId,
                Channel = Channel,
                Payload = Payload,
                Reason = Reason,
                FailedAtUtcTicks = FailedAtUtcTicks,
                Attempts = Attempts,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                TtlUnixSeconds = ttlUnixSeconds,
                MetadataJson = MetadataJson
            };
        }
    }
}
