// src/Infrastructure/Storage/Dynamo/PubSubMessageEntity.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Representation of a persisted pub/sub message stored in the shared single-table DynamoDB.
    /// Conventions:
    ///  - PK: id (string) — typically "msg:{messageId}" or "msg:{channel}:{messageId}"
    ///  - type: discriminator (e.g., "message" or "pubsub")
    ///  - channel: logical channel or topic (string)
    ///  - messageId: unique message identifier (string)
    ///  - payload: message body (string)
    ///  - targetType / targetId: optional routing hints (string)
    ///  - createdAtUtc: ticks (number)
    ///  - deliveredAtUtc: ticks (number) — optional
    ///  - failedAtUtc: ticks (number) — optional
    ///  - attempts: delivery attempt counter (number)
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    /// </summary>
    public sealed class PubSubMessageEntity
    {
        public const string TypeDiscriminator = "message";

        public string Id { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public string MessageId { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public string? TargetType { get; init; }
        public string? TargetId { get; init; }
        //public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        // optional: expose ticks if you need it
        // Persisted ticks column; private setter keeps EF happy and keeps values in sync
        public long CreatedAtUtcTicks
        {
            get => CreatedAtUtc.Ticks;
            private set => CreatedAtUtc = new DateTime(value, DateTimeKind.Utc);
        }
        public long? DeliveredAtUtcTicks { get; init; }
        public long? FailedAtUtcTicks { get; init; }
        public int Attempts { get; init; } = 0;
        public long? TtlUnixSeconds { get; init; }

        public PubSubMessageEntity() { }

        public PubSubMessageEntity(string id, string channel, string messageId, string payload,
            string? targetType = null, string? targetId = null, long? ttlUnixSeconds = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            Payload = payload ?? string.Empty;
            TargetType = targetType;
            TargetId = targetId;
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
            TtlUnixSeconds = ttlUnixSeconds;
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
                ["channel"] = new AttributeValue { S = Channel },
                ["messageId"] = new AttributeValue { S = MessageId },
                ["payload"] = new AttributeValue { S = Payload },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() },
                ["attempts"] = new AttributeValue { N = Attempts.ToString() }
            };

            if (!string.IsNullOrWhiteSpace(TargetType))
                item["targetType"] = new AttributeValue { S = TargetType };

            if (!string.IsNullOrWhiteSpace(TargetId))
                item["targetId"] = new AttributeValue { S = TargetId };

            if (DeliveredAtUtcTicks.HasValue)
                item["deliveredAtUtc"] = new AttributeValue { N = DeliveredAtUtcTicks.Value.ToString() };

            if (FailedAtUtcTicks.HasValue)
                item["failedAtUtc"] = new AttributeValue { N = FailedAtUtcTicks.Value.ToString() };

            if (TtlUnixSeconds.HasValue)
                item["ttl"] = new AttributeValue { N = TtlUnixSeconds.Value.ToString() };

            return item;
        }

        /// <summary>
        /// Try to create a PubSubMessageEntity from a DynamoDB item. Returns null if the item is not a message entity.
        /// </summary>
        public static PubSubMessageEntity? FromItem(IDictionary<string, AttributeValue>? item)
        {
            if (item == null || item.Count == 0) return null;

            if (!item.TryGetValue("type", out var typeAttr) || typeAttr.S != TypeDiscriminator) return null;
            if (!item.TryGetValue("id", out var idAttr) || idAttr.S == null) return null;
            if (!item.TryGetValue("messageId", out var msgIdAttr) || msgIdAttr.S == null) return null;

            var id = idAttr.S!;
            var messageId = msgIdAttr.S!;
            item.TryGetValue("channel", out var channelAttr);
            item.TryGetValue("payload", out var payloadAttr);
            item.TryGetValue("targetType", out var targetTypeAttr);
            item.TryGetValue("targetId", out var targetIdAttr);
            item.TryGetValue("createdAtUtc", out var createdAttr);
            item.TryGetValue("deliveredAtUtc", out var deliveredAttr);
            item.TryGetValue("failedAtUtc", out var failedAttr);
            item.TryGetValue("attempts", out var attemptsAttr);
            item.TryGetValue("ttl", out var ttlAttr);

            var channel = channelAttr?.S ?? string.Empty;
            var payload = payloadAttr?.S ?? string.Empty;
            var targetType = targetTypeAttr?.S;
            var targetId = targetIdAttr?.S;

            long createdTicks = DateTime.UtcNow.Ticks;
            if (createdAttr?.N != null && long.TryParse(createdAttr.N, out var parsedCreated))
                createdTicks = parsedCreated;

            long? deliveredTicks = null;
            if (deliveredAttr?.N != null && long.TryParse(deliveredAttr.N, out var parsedDelivered))
                deliveredTicks = parsedDelivered;

            long? failedTicks = null;
            if (failedAttr?.N != null && long.TryParse(failedAttr.N, out var parsedFailed))
                failedTicks = parsedFailed;

            int attempts = 0;
            if (attemptsAttr?.N != null && int.TryParse(attemptsAttr.N, out var parsedAttempts))
                attempts = parsedAttempts;

            long? ttl = null;
            if (ttlAttr?.N != null && long.TryParse(ttlAttr.N, out var parsedTtl))
                ttl = parsedTtl;

            return new PubSubMessageEntity
            {
                Id = id,
                Channel = channel,
                MessageId = messageId,
                Payload = payload,
                TargetType = targetType,
                TargetId = targetId,
                CreatedAtUtcTicks = createdTicks,
                DeliveredAtUtcTicks = deliveredTicks,
                FailedAtUtcTicks = failedTicks,
                Attempts = attempts,
                TtlUnixSeconds = ttl
            };
        }

        /// <summary>
        /// Build a canonical id for a message. Useful for consistent primary keys.
        /// Example: "msg:{channel}:{messageId}" or "msg:{messageId}" if channel is empty.
        /// </summary>
        public static string MakeId(string channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channel)) return $"msg:{messageId}";
            return $"msg:{channel}:{messageId}";
        }

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        //public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);
        // EF can set this via the private setter
        public DateTime CreatedAtUtc { get; private set; }

        /// <summary>
        /// Returns true if the message is expired according to ttl (if present).
        /// </summary>
        public bool IsExpired()
        {
            if (!TtlUnixSeconds.HasValue) return false;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return TtlUnixSeconds.Value < now;
        }

        /// <summary>
        /// Return a copy with attempts incremented and optional delivered/failed timestamps set.
        /// This is a convenience for optimistic-update patterns.
        /// </summary>
        public PubSubMessageEntity WithAttemptIncremented(bool markDelivered = false, bool markFailed = false)
        {
            var delivered = DeliveredAtUtcTicks;
            var failed = FailedAtUtcTicks;
            if (markDelivered) delivered = DateTime.UtcNow.Ticks;
            if (markFailed) failed = DateTime.UtcNow.Ticks;

            return new PubSubMessageEntity
            {
                Id = Id,
                Channel = Channel,
                MessageId = MessageId,
                Payload = Payload,
                TargetType = TargetType,
                TargetId = TargetId,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                DeliveredAtUtcTicks = delivered,
                FailedAtUtcTicks = failed,
                Attempts = Attempts + 1,
                TtlUnixSeconds = TtlUnixSeconds
            };
        }
    }
}
