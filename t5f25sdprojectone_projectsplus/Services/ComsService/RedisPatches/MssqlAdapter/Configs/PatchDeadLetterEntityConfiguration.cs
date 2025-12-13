// src/Infrastructure/Storage/Dynamo/PatchDeadLetterEntityConfiguration.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter.Configs
{
    /// <summary>
    /// Helper / configuration utilities for persisting PatchDeadLetterEntity instances into the
    /// shared single-table DynamoDB layout used by the comms stack.
    ///
    /// Centralizes attribute names, common request builders (Get/Put/Update/Delete/Query),
    /// and small helpers for TTL handling.
    /// </summary>
    public static class PatchDeadLetterEntityConfiguration
    {
        // Attribute names used in the table
        public const string AttrId = "id";
        public const string AttrType = "type";
        public const string AttrOriginalMessageId = "originalMessageId";
        public const string AttrChannel = "channel";
        public const string AttrPayload = "payload";
        public const string AttrReason = "reason";
        public const string AttrFailedAtUtc = "failedAtUtc";
        public const string AttrAttempts = "attempts";
        public const string AttrCreatedAtUtc = "createdAtUtc";
        public const string AttrTtl = "ttl";
        public const string AttrMetadata = "metadata";

        /// <summary>
        /// Type discriminator value for dead-letter entities.
        /// </summary>
        public const string TypeDiscriminator = PatchDeadLetterEntity.TypeDiscriminator;

        /// <summary>
        /// Projection expression used for most reads.
        /// </summary>
        private const string DefaultProjection = AttrId + ", " + AttrType + ", " + AttrOriginalMessageId + ", " +
                                                 AttrChannel + ", " + AttrPayload + ", " + AttrReason + ", " +
                                                 AttrFailedAtUtc + ", " + AttrAttempts + ", " + AttrCreatedAtUtc + ", " +
                                                 AttrTtl + ", " + AttrMetadata;

        /// <summary>
        /// Build a GetItemRequest for the given dead-letter id.
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
        /// Build a PutItemRequest to insert or replace a PatchDeadLetterEntity.
        /// </summary>
        public static PutItemRequest BuildPutRequest(string tableName, PatchDeadLetterEntity entity)
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
        public static PutItemRequest BuildPutIfAttemptsMatches(string tableName, PatchDeadLetterEntity entity, int? expectedAttempts)
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
        /// Build an UpdateItemRequest that increments attempts and optionally updates reason/failedAt and metadata.
        /// Returns ALL_NEW so callers can read the updated item.
        /// </summary>
        public static UpdateItemRequest BuildIncrementAttemptsRequest(
            string tableName,
            string id,
            string? reason = null,
            bool updateFailedAt = true,
            string? metadataJson = null,
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
                [":inc"] = new AttributeValue { N = "1" },
                [":zero"] = new AttributeValue { N = "0" }
            };

            var setParts = new List<string>
            {
                "#attempts = if_not_exists(#attempts, :zero) + :inc"
            };

            if (updateFailedAt)
            {
                exprNames["#failed"] = AttrFailedAtUtc;
                exprValues[":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() };
                setParts.Add("#failed = :nowTicks");
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                exprNames["#reason"] = AttrReason;
                exprValues[":reason"] = new AttributeValue { S = reason };
                setParts.Add("#reason = :reason");
            }

            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                exprNames["#meta"] = AttrMetadata;
                exprValues[":meta"] = new AttributeValue { S = metadataJson };
                setParts.Add("#meta = :meta");
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
        /// Build a QueryRequest to list dead-letter entries for a channel ordered by createdAtUtc (requires ChannelCreatedAtIndex).
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
        /// Try to parse a PatchDeadLetterEntity from a DynamoDB item dictionary.
        /// Returns null if the item is not a dead-letter entity.
        /// </summary>
        public static PatchDeadLetterEntity? TryParse(IDictionary<string, AttributeValue>? item)
        {
            return PatchDeadLetterEntity.FromItem(item);
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
