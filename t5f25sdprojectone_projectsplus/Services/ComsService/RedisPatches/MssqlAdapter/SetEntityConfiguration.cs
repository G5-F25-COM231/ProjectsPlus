// src/Infrastructure/Storage/Dynamo/SetEntityConfiguration.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Helper / configuration utilities for persisting SetEntity instances into the
    /// shared single-table DynamoDB layout used by the comms stack.
    ///
    /// Centralizes attribute names, common request builders (Get/Put/Update/Delete),
    /// and small helpers for optimistic concurrency (version) and TTL handling.
    /// </summary>
    public static class SetEntityConfiguration
    {
        // Attribute names used in the table
        public const string AttrId = "id";
        public const string AttrType = "type";
        public const string AttrKey = "key";
        public const string AttrMembers = "members";
        public const string AttrCreatedAtUtc = "createdAtUtc";
        public const string AttrTtl = "ttl";
        public const string AttrVersion = "version";

        /// <summary>
        /// Type discriminator value for set entities.
        /// </summary>
        public const string TypeDiscriminator = SetEntity.TypeDiscriminator;

        /// <summary>
        /// Build a GetItemRequest for the given set id.
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
                ProjectionExpression = $"{AttrId}, {AttrType}, {AttrKey}, {AttrMembers}, {AttrCreatedAtUtc}, {AttrTtl}, {AttrVersion}"
            };
        }

        /// <summary>
        /// Build a PutItemRequest to insert or replace a SetEntity.
        /// If you want optimistic concurrency, use BuildPutIfVersionMatches instead.
        /// </summary>
        public static PutItemRequest BuildPutRequest(string tableName, SetEntity entity)
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
        public static PutItemRequest BuildPutIfVersionMatches(string tableName, SetEntity entity, long? expectedVersion)
        {
            var req = BuildPutRequest(tableName, entity);

            if (expectedVersion.HasValue)
            {
                req.ConditionExpression = $"attribute_exists({AttrId}) AND {AttrVersion} = :expectedVer";
                req.ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":expectedVer"] = new AttributeValue { N = expectedVersion.Value.ToString() }
                };
            }
            else
            {
                req.ConditionExpression = $"attribute_not_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that replaces the members set and bumps the version.
        /// Use this when you want to set the full members collection atomically.
        /// </summary>
        public static UpdateItemRequest BuildReplaceMembersRequest(
            string tableName,
            string id,
            IEnumerable<string> members,
            long? newVersion,
            long? expectedVersion = null,
            long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (members == null) throw new ArgumentNullException(nameof(members));

            var exprNames = new Dictionary<string, string>
            {
                ["#members"] = AttrMembers,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":members"] = new AttributeValue { SS = new List<string>(members) },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            var setParts = new List<string> { "#members = :members", "#ver = :ver" };

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
                req.ConditionExpression = $"attribute_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that adds a member to the string set (idempotent).
        /// Uses the ADD operator to add to a string set attribute.
        /// If the members attribute does not exist, ADD will create it as a set.
        /// </summary>
        public static UpdateItemRequest BuildAddMemberRequest(
            string tableName,
            string id,
            string member,
            long? newVersion = null,
            long? expectedVersion = null,
            long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(member)) throw new ArgumentNullException(nameof(member));

            var exprNames = new Dictionary<string, string>
            {
                ["#members"] = AttrMembers,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":m"] = new AttributeValue { SS = new List<string> { member } },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            var updateParts = new List<string> { "ADD #members :m", "SET #ver = :ver" };

            if (ttlUnixSeconds.HasValue)
            {
                exprNames["#ttl"] = AttrTtl;
                exprValues[":ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
                updateParts.Add("#ttl = :ttl");
            }

            var updateExpr = string.Join(" ", updateParts);

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
                req.ConditionExpression = $"attribute_exists({AttrId})";
            }

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that removes a member from the string set (idempotent).
        /// Uses the DELETE operator to remove from a string set attribute.
        /// </summary>
        public static UpdateItemRequest BuildRemoveMemberRequest(
            string tableName,
            string id,
            string member,
            long? newVersion = null,
            long? expectedVersion = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(member)) throw new ArgumentNullException(nameof(member));

            var exprNames = new Dictionary<string, string>
            {
                ["#members"] = AttrMembers,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":m"] = new AttributeValue { SS = new List<string> { member } },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            // Use DELETE to remove member from set, and update version
            var updateExpr = "DELETE #members :m SET #ver = :ver";

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
        /// Try to parse a SetEntity from a DynamoDB item dictionary.
        /// Returns null if the item is not a set entity.
        /// </summary>
        public static SetEntity? TryParse(IDictionary<string, AttributeValue>? item)
        {
            return SetEntity.FromItem(item);
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
