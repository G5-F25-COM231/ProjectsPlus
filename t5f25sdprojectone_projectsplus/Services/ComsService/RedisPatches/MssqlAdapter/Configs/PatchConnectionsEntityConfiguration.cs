// src/Infrastructure/Storage/Dynamo/PatchConnectionsEntityConfiguration.cs
using System;
using System.Collections.Generic;
using Amazon.DynamoDBv2.Model;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter.Configs
{
    /// <summary>
    /// Helper / configuration utilities for persisting PatchConnectionEntity instances into the
    /// shared single-table DynamoDB layout used by the comms stack.
    ///
    /// Centralizes attribute names, common request builders (Get/Put/Update/Delete/Query),
    /// and small helpers for optimistic concurrency (version) and TTL handling.
    /// </summary>
    public static class PatchConnectionsEntityConfiguration
    {
        // Attribute names used in the table
        public const string AttrId = "id";
        public const string AttrType = "type";
        public const string AttrConnectionId = "connectionId";
        public const string AttrUserId = "userId";
        public const string AttrOwnerInstance = "ownerInstance";
        public const string AttrRooms = "rooms";
        public const string AttrCreatedAtUtc = "createdAtUtc";
        public const string AttrLastHeartbeatUtc = "lastHeartbeatUtc";
        public const string AttrTtl = "ttl";
        public const string AttrVersion = "version";

        /// <summary>
        /// Type discriminator value for connection entities.
        /// </summary>
        public const string TypeDiscriminator = PatchConnectionEntity.TypeDiscriminator;

        /// <summary>
        /// Projection expression used for most reads.
        /// </summary>
        private const string DefaultProjection = AttrId + ", " + AttrType + ", " + AttrConnectionId + ", " +
                                                 AttrUserId + ", " + AttrOwnerInstance + ", " + AttrRooms + ", " +
                                                 AttrCreatedAtUtc + ", " + AttrLastHeartbeatUtc + ", " + AttrTtl + ", " + AttrVersion;

        /// <summary>
        /// Build a GetItemRequest for the given connection id.
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
        /// Build a PutItemRequest to insert or replace a PatchConnectionEntity.
        /// </summary>
        public static PutItemRequest BuildPutRequest(string tableName, PatchConnectionEntity entity)
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
        public static PutItemRequest BuildPutIfVersionMatches(string tableName, PatchConnectionEntity entity, long? expectedVersion)
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
        /// Build an UpdateItemRequest that sets/updates the heartbeat timestamp to now.
        /// Optionally bump version and set TTL.
        /// </summary>
        public static UpdateItemRequest BuildHeartbeatRequest(string tableName, string id, long? newVersion = null, long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var exprNames = new Dictionary<string, string>
            {
                ["#hb"] = AttrLastHeartbeatUtc
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":nowTicks"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
            };

            var setParts = new List<string> { "#hb = :nowTicks" };

            if (newVersion.HasValue)
            {
                exprNames["#ver"] = AttrVersion;
                exprValues[":ver"] = new AttributeValue { N = newVersion.Value.ToString() };
                setParts.Add("#ver = :ver");
            }

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
        /// Build an UpdateItemRequest that sets the ownerInstance (claim or release).
        /// If ownerInstance is null the attribute will be removed.
        /// Optionally bump version and set TTL.
        /// </summary>
        public static UpdateItemRequest BuildSetOwnerRequest(string tableName, string id, string? ownerInstance, long? newVersion = null, long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            var exprNames = new Dictionary<string, string>();
            var exprValues = new Dictionary<string, AttributeValue>();
            var updateParts = new List<string>();

            if (ownerInstance != null)
            {
                exprNames["#owner"] = AttrOwnerInstance;
                exprValues[":owner"] = new AttributeValue { S = ownerInstance };
                updateParts.Add("#owner = :owner");
            }
            else
            {
                // remove owner attribute
                updateParts.Add("REMOVE " + AttrOwnerInstance);
            }

            if (newVersion.HasValue)
            {
                exprNames["#ver"] = AttrVersion;
                exprValues[":ver"] = new AttributeValue { N = newVersion.Value.ToString() };
                // If we used REMOVE above, we need a SET for version; combine appropriately
                if (ownerInstance != null)
                    updateParts.Add("#ver = :ver");
                else
                    updateParts.Add("SET #ver = :ver");
            }

            if (ttlUnixSeconds.HasValue)
            {
                exprNames["#ttl"] = AttrTtl;
                exprValues[":ttl"] = new AttributeValue { N = ttlUnixSeconds.Value.ToString() };
                // If last part is REMOVE, we must ensure SET is present; prefer to append SET clause
                updateParts.Add("#ttl = :ttl");
            }

            // Build final expression: DynamoDB does not allow mixing REMOVE and SET in arbitrary order;
            // we will construct SET parts and REMOVE parts separately.
            var setParts = new List<string>();
            var removeParts = new List<string>();

            foreach (var part in updateParts)
            {
                if (part.StartsWith("REMOVE ", StringComparison.Ordinal))
                {
                    removeParts.Add(part.Substring("REMOVE ".Length));
                }
                else if (part.StartsWith("SET ", StringComparison.Ordinal))
                {
                    setParts.Add(part.Substring("SET ".Length));
                }
                else if (part.Contains(" = "))
                {
                    setParts.Add(part);
                }
                else
                {
                    // fallback: treat as set
                    setParts.Add(part);
                }
            }

            var expr = string.Empty;
            if (setParts.Count > 0) expr += "SET " + string.Join(", ", setParts);
            if (removeParts.Count > 0)
            {
                if (!string.IsNullOrEmpty(expr)) expr += " ";
                expr += "REMOVE " + string.Join(", ", removeParts);
            }

            var req = new UpdateItemRequest
            {
                TableName = tableName,
                Key = new Dictionary<string, AttributeValue> { [AttrId] = new AttributeValue { S = id } },
                ExpressionAttributeNames = exprNames.Count > 0 ? exprNames : null,
                ExpressionAttributeValues = exprValues.Count > 0 ? exprValues : null,
                UpdateExpression = expr,
                ReturnValues = "ALL_NEW",
                ConditionExpression = $"attribute_exists({AttrId})"
            };

            return req;
        }

        /// <summary>
        /// Build an UpdateItemRequest that adds a room to the rooms string set (idempotent).
        /// Uses ADD to add to a string set attribute. Optionally bump version and set TTL.
        /// </summary>
        public static UpdateItemRequest BuildAddRoomRequest(string tableName, string id, string room, long? newVersion = null, long? expectedVersion = null, long? ttlUnixSeconds = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));

            var exprNames = new Dictionary<string, string>
            {
                ["#rooms"] = AttrRooms,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":r"] = new AttributeValue { SS = new List<string> { room } },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            var updateParts = new List<string> { "ADD #rooms :r", "SET #ver = :ver" };

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
        /// Build an UpdateItemRequest that removes a room from the rooms string set (idempotent).
        /// Uses DELETE to remove from a string set attribute. Optionally bump version.
        /// </summary>
        public static UpdateItemRequest BuildRemoveRoomRequest(string tableName, string id, string room, long? newVersion = null, long? expectedVersion = null)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));

            var exprNames = new Dictionary<string, string>
            {
                ["#rooms"] = AttrRooms,
                ["#ver"] = AttrVersion
            };

            var exprValues = new Dictionary<string, AttributeValue>
            {
                [":r"] = new AttributeValue { SS = new List<string> { room } },
                [":ver"] = new AttributeValue { N = (newVersion ?? 0).ToString() }
            };

            // DELETE #rooms :r SET #ver = :ver
            var updateExpr = "DELETE #rooms :r SET #ver = :ver";

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
        /// Build a QueryRequest to list connections for a given user (requires UserIdIndex).
        /// Caller may set Limit/ExclusiveStartKey as needed.
        /// </summary>
        public static QueryRequest BuildQueryByUserRequest(string tableName, string userId, int limit = 50, bool ascending = true)
        {
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));
            if (userId == null) throw new ArgumentNullException(nameof(userId));

            var req = new QueryRequest
            {
                TableName = tableName,
                IndexName = "UserIdIndex",
                KeyConditionExpression = "#uid = :uid",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#uid"] = AttrUserId },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":uid"] = new AttributeValue { S = userId } },
                ProjectionExpression = DefaultProjection,
                Limit = limit,
                ScanIndexForward = ascending
            };

            return req;
        }

        /// <summary>
        /// Try to parse a PatchConnectionEntity from a DynamoDB item dictionary.
        /// Returns null if the item is not a connection entity.
        /// </summary>
        public static PatchConnectionEntity? TryParse(IDictionary<string, AttributeValue>? item)
        {
            return PatchConnectionEntity.FromItem(item);
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
