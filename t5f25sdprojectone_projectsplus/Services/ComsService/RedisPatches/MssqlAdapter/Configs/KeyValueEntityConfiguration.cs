// src/Infrastructure/Storage/Dynamo/KeyValueEntityConfiguration.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter.Configs
{
    /// <summary>
    /// Helper / configuration utilities for persisting KeyValueEntity instances into the
    /// shared single-table DynamoDB layout used by the comms stack.
    /// 
    /// This class centralizes attribute names, common request builders (Get/Put/Update/Delete),
    /// and small helpers for optimistic concurrency (version) and TTL handling.
    /// </summary>
    public static class KeyValueEntityConfiguration
    {
        // Attribute names used in the table
        public const string AttrId = "id";
        public const string AttrType = "type";
        public const string AttrKey = "key";
        public const string AttrValue = "value";
        public const string AttrCreatedAtUtc = "createdAtUtc";
        public const string AttrTtl = "ttl";
        public const string AttrVersion = "version";

        /// <summary>
        /// Type discriminator value for key/value entities.
        /// </summary>
        public const string TypeDiscriminator = KeyValueEntity.TypeDiscriminator;

        /// <summary>
        /// Build a GetItemRequest for the given logical key id.
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
                ProjectionExpression = $"{AttrId}, {AttrType}, {AttrKey}, {AttrValue}, {AttrCreatedAtUtc}, {AttrTtl}, {AttrVersion}"
            };
        }

        /// <summary>
        /// Build a PutItemRequest to insert or replace a KeyValueEntity.
        /// If you want optimistic concurrency, use BuildPutIfVersionMatches instead.
        /// </summary>
        public static PutItemRequest BuildPutRequest(string tableName, KeyValueEntity entity)
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
        /// Build a PutItemRequest that enforces an expected version (optimistic concurrency).
        /// If expectedVersion is null, the request will only succeed if the item does not exist.
        /// </summary>
        public static PutItemRequest BuildPutIfVersionMatches(string tableName, KeyValueEntity entity, long? expectedVersion)
        {
            var req = BuildPutRequest(tableName, entity);

            if (expectedVersion.HasValue)
            {
                // Expect existing version to equal expectedVersion
                req.ConditionExpression = $"attribute_exists({AttrId}) AND {AttrVersion} = :expectedVer";
                req.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedVer"] = new AttributeValue { N = expectedVersion.Value.ToString() }
                };
            }
            else
            {
                // Expect item to not exist
                req.ConditionExpression = $"attribute_not_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest to update the value and bump the version atomically.
        /// If expectedVersion is provided, the update will only succeed when the current version matches.
        /// If expectedVersion is null, the update will only succeed if the item exists (no version check).
        /// </summary>
        public static UpdateItemRequest BuildUpdateValueRequest(
            string tableName,
            string id,
            string newValue,
            long? newVersion,
            long? expectedVersion = null,
            long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (newValue == null) throw new ArgumentNullException(nameof(newValue));

            var exprNames = new Dictionary<string, string>
            {
                ["#v"] = AttrValue,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":val"] = new AttributeValue { S = newValue },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            var setParts = new List<string> { "#v = :val", "#ver = :ver" };

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
                ReturnValues = "ALL_NEW"
            };

            if (expectedVersion.HasValue)
            {
                req.ConditionExpression = $"attribute_exists({AttrId}) AND {AttrVersion} = :expectedVer";
                req.ExpressionAttributeValues[":expectedVer"] = new AttributeValue { N = expectedVersion.Value.ToString() };
            }
            else
            {
                // Ensure item exists
                req.ConditionExpression = $"attribute_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build a DeleteItemRequest for the given id. If expectedVersion is provided, the delete will be conditional.
        /// </summary>
        public static DeleteItemRequest BuildDeleteRequest(string tableName, string id, long? expectedVersion = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var req = new DeleteItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } }
            };

            if (expectedVersion.HasValue)
            {
                req.ConditionExpression = $"{AttrVersion} = :expectedVer";
                req.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedVer"] = new AttributeValue { N = expectedVersion.Value.ToString() }
                };
            }

            return req;
        }

        /// <summary>
        /// Try to parse a KeyValueEntity from a DynamoDB item dictionary.
        /// Returns null if the item is not a key/value entity.
        /// </summary>
        public static KeyValueEntity? TryParse(IDictionary<string, AttributeValue>? item)
        {
            return KeyValueEntity.FromItem(item);
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

        /// <summary>
        /// Build a minimal PutItemRequest that creates a marker for a logical key (useful for idempotency markers).
        /// The marker id is typically "kv:{key}" or a custom id passed in.
        /// </summary>
        public static PutItemRequest BuildMarkerPutRequest(string tableName, string id, string key, long? ttlUnixSeconds = null, long? version = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));

            var item = new Dictionary<string, AttributeValue>
            {
                [AttrId] = new AttributeValue { S = id },
                [AttrType] = new AttributeValue { S = TypeDiscriminator },
                [AttrKey] = new AttributeValue { S = key },
                [AttrCreatedAtUtc] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
            };

            if (ttlUnixSeconds.HasValue) item[AttrTtl] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
            if (version.HasValue) item[AttrVersion] = new AttributeValue { N = version.Value.ToString() };

            return new PutItemRequest
            {
                TableName = tableName,
                Item = item,
                ConditionExpression = "attribute_not_exists(id)" // create-only marker by default
            };
        }
    }
}
