// src/Infrastructure/Storage/Dynamo/PatchMessageEntityConfiguration.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter.Configs
{
    /// <summary>
    /// Helper / configuration utilities for persisting PatchMessageEntity instances into the
    /// shared single-table DynamoDB layout used by the comms stack.
    ///
    /// Centralizes attribute names, common request builders (Get/Put/Update/Delete/Query),
    /// and small helpers for optimistic concurrency (attempts/status) and TTL handling.
    /// </summary>
    public static class PatchMessageEntityConfiguration
    {
        // Attribute names used in the table
        public const string AttrId = "id";
        public const string AttrType = "type";
        public const string AttrChannel = "channel";
        public const string AttrMessageId = "messageId";
        public const string AttrPayload = "payload";
        public const string AttrTargetType = "targetType";
        public const string AttrTargetId = "targetId";
        public const string AttrCreatedAtUtc = "createdAtUtc";
        public const string AttrDeliveredAtUtc = "deliveredAtUtc";
        public const string AttrFailedAtUtc = "failedAtUtc";
        public const string AttrAttempts = "attempts";
        public const string AttrTtl = "ttl";
        public const string AttrStatus = "status";

        /// <summary>
        /// Type discriminator value for message entities.
        /// </summary>
        public const string TypeDiscriminator = PatchMessageEntity.TypeDiscriminator;

        /// <summary>
        /// Projection expression used for most reads.
        /// </summary>
        private const string DefaultProjection = AttrId + ", " + AttrType + ", " + AttrChannel + ", " + AttrMessageId + ", " +
                                                 AttrPayload + ", " + AttrTargetType + ", " + AttrTargetId + ", " +
                                                 AttrCreatedAtUtc + ", " + AttrDeliveredAtUtc + ", " + AttrFailedAtUtc + ", " +
                                                 AttrAttempts + ", " + AttrTtl + ", " + AttrStatus;

        /// <summary>
        /// Build a GetItemRequest for the given message id.
        /// </summary>
        public static GetItemRequest BuildGetRequest(string tableName, string id, bool consistentRead = false)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            return new GetItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } },
                ConsistentRead = consistentRead,
                ProjectionExpression = DefaultProjection
            };
        }

        /// <summary>
        /// Build a PutItemRequest to insert or replace a PatchMessageEntity.
        /// </summary>
        public static PutItemRequest BuildPutRequest(string tableName, PatchMessageEntity entity)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            return new PutItemRequest
            {
                TableName = tableName,
                Item = entity.ToItem()
            };
        }

        /// <summary>
        /// Build a PutItemRequest that enforces create-only or expected attempts semantics.
        /// If expectedAttempts is null the request will be create-only (attribute_not_exists(id)).
        /// If expectedAttempts has a value, the request will require attempts to equal that value (optimistic).
        /// </summary>
        public static PutItemRequest BuildPutIfAttemptsMatches(string tableName, PatchMessageEntity entity, int? expectedAttempts)
        {
            var req = BuildPutRequest(tableName, entity);

            if (expectedAttempts.HasValue)
            {
                req.ConditionExpression = $"attribute_exists({AttrId}) AND {AttrAttempts} = :expectedAttempts";
                req.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedAttempts"] = new AttributeValue { N = expectedAttempts.Value.ToString() }
                };
            }
            else
            {
                req.ConditionExpression = $"attribute_not_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that increments attempts and optionally sets delivered/failed timestamps and status.
        /// Returns ALL_NEW so callers can read the updated item.
        /// </summary>
        public static UpdateItemRequest BuildIncrementAttemptsRequest(
            string tableName,
            string id,
            bool markDelivered = false,
            bool markFailed = false,
            string? status = null,
            long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var exprNames = new Dictionary<string, string>
            {
                ["#attempts"] = AttrAttempts
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":inc"] = new AttributeValue { N = "1" }
            };

            var setParts = new List<string>
            {
                "#attempts = if_not_exists(#attempts, :zero) + :inc"
            };

            exprValues[":zero"] = new AttributeValue { N = "0" };

            if (markDelivered)
            {
                exprNames["#del"] = AttrDeliveredAtUtc;
                exprValues[":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() };
                setParts.Add("#del = :nowTicks");
            }

            if (markFailed)
            {
                exprNames["#fail"] = AttrFailedAtUtc;
                if (!exprValues.ContainsKey(":nowTicks"))
                    exprValues[":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() };
                setParts.Add("#fail = :nowTicks");
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                exprNames["#status"] = AttrStatus;
                exprValues[":status"] = new AttributeValue { S = status };
                setParts.Add("#status = :status");
            }

            if (ttlUnixSeconds.HasValue)
            {
                exprNames["#ttl"] = AttrTtl;
                exprValues[":ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
                setParts.Add("#ttl = :ttl");
            }

            var updateExpr = "SET " + string.Join(", ", setParts);

            var req = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } },
                ExpressionAttributeNames = exprNames,
                ExpressionAttributeValues = exprValues,
                UpdateExpression = updateExpr,
                ReturnValues = "ALL_NEW",
                ConditionExpression = $"attribute_exists({AttrId})"
            };

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that marks the message as delivered (sets delivered timestamp and optional status).
        /// </summary>
        public static UpdateItemRequest BuildMarkDeliveredRequest(string tableName, string id, string? status = "delivered", long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var exprNames = new Dictionary<string, string>
            {
                ["#del"] = AttrDeliveredAtUtc,
                ["#status"] = AttrStatus
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() },
                [":status"] = new AttributeValue { S = status ?? "delivered" }
            };

            var setParts = new List<string> { "#del = :nowTicks", "#status = :status" };

            if (ttlUnixSeconds.HasValue)
            {
                exprNames["#ttl"] = AttrTtl;
                exprValues[":ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
                setParts.Add("#ttl = :ttl");
            }

            var req = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } },
                ExpressionAttributeNames = exprNames,
                ExpressionAttributeValues = exprValues,
                UpdateExpression = "SET " + string.Join(", ", setParts),
                ReturnValues = "ALL_NEW",
                ConditionExpression = $"attribute_exists({AttrId})"
            };

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that marks the message as failed (sets failed timestamp and optional status).
        /// </summary>
        public static UpdateItemRequest BuildMarkFailedRequest(string tableName, string id, string? status = "failed", long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var exprNames = new Dictionary<string, string>
            {
                ["#fail"] = AttrFailedAtUtc,
                ["#status"] = AttrStatus
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() },
                [":status"] = new AttributeValue { S = status ?? "failed" }
            };

            var setParts = new List<string> { "#fail = :nowTicks", "#status = :status" };

            if (ttlUnixSeconds.HasValue)
            {
                exprNames["#ttl"] = AttrTtl;
                exprValues[":ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
                setParts.Add("#ttl = :ttl");
            }

            var req = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } },
                ExpressionAttributeNames = exprNames,
                ExpressionAttributeValues = exprValues,
                UpdateExpression = "SET " + string.Join(", ", setParts),
                ReturnValues = "ALL_NEW",
                ConditionExpression = $"attribute_exists({AttrId})"
            };

            return req;
        }

        /// <summary>
        /// Build a DeleteItemRequest for the given id. If requireExists is true the delete will be conditional.
        /// </summary>
        public static DeleteItemRequest BuildDeleteRequest(string tableName, string id, bool requireExists = true)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var req = new DeleteItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } }
            };

            if (requireExists)
            {
                req.ConditionExpression = $"attribute_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build a QueryRequest to list messages for a channel ordered by createdAtUtc (requires ChannelCreatedAtIndex).
        /// Caller may set Limit/ExclusiveStartKey as needed.
        /// </summary>
        public static QueryRequest BuildQueryByChannelRequest(string tableName, string channel, int limit = 50, bool ascending = false)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (channel == null) throw new ArgumentNullException(nameof(channel));

            var req = new QueryRequest
            {
                TableName = tableName,
                IndexName = "ChannelCreatedAtIndex",
                KeyConditionExpression = "#ch = :ch",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#ch"] = AttrChannel },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":ch"] = new AttributeValue { S = channel } },
                ProjectionExpression = DefaultProjection,
                Limit = limit,
                ScanIndexForward = ascending
            };

            return req;
        }

        /// <summary>
        /// Try to parse a PatchMessageEntity from a DynamoDB item dictionary.
        /// Returns null if the item is not a message entity.
        /// </summary>
        public static PatchMessageEntity? TryParse(IDictionary<string, AttributeValue>? item)
        {
            return PatchMessageEntity.FromItem(item);
        }

        /// <summary>
        /// Compute TTL unix epoch seconds from a retention TimeSpan relative to now.
        /// Returns null if retention is null or non-positive.
        /// </summary>
        public static long? ComputeTtlUnixSeconds(TimeSpan? retention)
        {
            if (!retention.HasValue || retention.Value <= TimeSpan.Zero) return null;
            return DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();
        }
    }
}
