// src/Infrastructure/Storage/Dynamo/MessageEntity.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Generic persisted message entity stored in the shared single-table DynamoDB.
    /// Conventions:
    ///  - PK: id (string) — typically "msg:{channel}:{messageId}" or "msg:{messageId}"
    ///  - type: discriminator (e.g., "message")
    ///  - channel: logical channel or topic (string)
    ///  - messageId: unique message identifier (string)
    ///  - payload: message body (string)
    ///  - targetType / targetId: optional routing hints (string)
    ///  - createdAtUtc: ticks (number)
    ///  - deliveredAtUtc: ticks (number) — optional
    ///  - failedAtUtc: ticks (number) — optional
    ///  - attempts: delivery attempt counter (number)
    ///  - ttl: unix epoch seconds (number) — optional, used by DynamoDB TTL
    ///  - status: optional string status (e.g., "queued","delivered","failed")
    /// </summary>
    public sealed class PatchMessageEntity
    {
        public const string TypeDiscriminator = "message";

        public string Id { get; init; } = string.Empty;
        public string Channel { get; init; } = string.Empty;
        public string MessageId { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
        public string? TargetType { get; init; }
        public string? TargetId { get; init; }
        public long CreatedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;
        public long? DeliveredAtUtcTicks { get; init; }
        public long? FailedAtUtcTicks { get; init; }
        public int Attempts { get; init; } = 0;
        public long? TtlUnixSeconds { get; init; }
        public string? Status { get; init; }

        public PatchMessageEntity() { }

        public PatchMessageEntity(string id, string channel, string messageId, string payload,
            string? targetType = null, string? targetId = null, long? ttlUnixSeconds = null, string? status = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Channel = channel ?? string.Empty;
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            Payload = payload ?? string.Empty;
            TargetType = targetType;
            TargetId = targetId;
            CreatedAtUtcTicks = DateTime.UtcNow.Ticks;
            TtlUnixSeconds = ttlUnixSeconds;
            Status = status;
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
                ["messageId"] = new AttributeValue { S = MessageId },
                ["payload"] = new AttributeValue { S = Payload },
                ["createdAtUtc"] = new AttributeValue { N = CreatedAtUtcTicks.ToString() },
                ["attempts"] = new AttributeValue { N = Attempts.ToString() }
            };

            if (!string.IsNullOrWhiteSpace(Channel))
                item["channel"] = new AttributeValue { S = Channel };

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

            if (!string.IsNullOrWhiteSpace(Status))
                item["status"] = new AttributeValue { S = Status };

            return item;
        }

        /// <summary>
        /// Try to create a MessageEntity from a DynamoDB item. Returns null if the item is not a message entity.
        /// </summary>
        public static PatchMessageEntity? FromItem(IDictionary<string, AttributeValue>? item)
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
            item.TryGetValue("status", out var statusAttr);

            var channel = channelAttr?.S ?? string.Empty;
            var payload = payloadAttr?.S ?? string.Empty;
            var targetType = targetTypeAttr?.S;
            var targetId = targetIdAttr?.S;
            var status = statusAttr?.S;

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

            return new PatchMessageEntity
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
                TtlUnixSeconds = ttl,
                Status = status
            };
        }

        /// <summary>
        /// Build a canonical id for a message. Example: "msg:{channel}:{messageId}" or "msg:{messageId}" if channel is empty.
        /// </summary>
        public static string MakeId(string channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channel)) return $"msg:{messageId}";
            return $"msg:{channel}:{messageId}";
        }

        /// <summary>
        /// Returns the CreatedAtUtc as a DateTime (UTC).
        /// </summary>
        public DateTime CreatedAtUtc => new DateTime(CreatedAtUtcTicks, DateTimeKind.Utc);

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
        /// </summary>
        public PatchMessageEntity WithAttemptIncremented(bool markDelivered = false, bool markFailed = false)
        {
            var delivered = DeliveredAtUtcTicks;
            var failed = FailedAtUtcTicks;
            if (markDelivered) delivered = DateTime.UtcNow.Ticks;
            if (markFailed) failed = DateTime.UtcNow.Ticks;

            return new PatchMessageEntity
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
                TtlUnixSeconds = TtlUnixSeconds,
                Status = Status
            };
        }

        /// <summary>
        /// Return a copy marked as delivered (sets delivered timestamp and optional status).
        /// </summary>
        public PatchMessageEntity MarkDelivered(string? status = "delivered")
        {
            return new PatchMessageEntity
            {
                Id = Id,
                Channel = Channel,
                MessageId = MessageId,
                Payload = Payload,
                TargetType = TargetType,
                TargetId = TargetId,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                DeliveredAtUtcTicks = DateTime.UtcNow.Ticks,
                FailedAtUtcTicks = FailedAtUtcTicks,
                Attempts = Attempts,
                TtlUnixSeconds = TtlUnixSeconds,
                Status = status
            };
        }

        /// <summary>
        /// Return a copy marked as failed (sets failed timestamp and optional status).
        /// </summary>
        public PatchMessageEntity MarkFailed(string? status = "failed")
        {
            return new PatchMessageEntity
            {
                Id = Id,
                Channel = Channel,
                MessageId = MessageId,
                Payload = Payload,
                TargetType = TargetType,
                TargetId = TargetId,
                CreatedAtUtcTicks = CreatedAtUtcTicks,
                DeliveredAtUtcTicks = DeliveredAtUtcTicks,
                FailedAtUtcTicks = DateTime.UtcNow.Ticks,
                Attempts = Attempts,
                TtlUnixSeconds = TtlUnixSeconds,
                Status = status
            };
        }
    }
}
