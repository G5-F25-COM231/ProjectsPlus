// ops/infra/DynamoDbTables.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Idempotent helper to create the DynamoDB table used by the comms stack.
    /// Table shape:
    ///  - PK: id (S)
    ///  - Attributes: type (S), channel (S), createdAtUtc (N), ttl (N)
    ///  - GSI: TypeCreatedAtIndex  => PK: type (S), SK: createdAtUtc (N)
    ///  - GSI: ChannelCreatedAtIndex => PK: channel (S), SK: createdAtUtc (N)
    ///  - TTL enabled on attribute "ttl"
    /// </summary>
    public static class DynamoDbTables
    {
        /// <summary>
        /// Create the table if it does not exist. Returns true if table was created or already exists.
        /// </summary>
        public static async Task<bool> CreateIfMissingAsync(IAmazonDynamoDB ddb, string tableName, ILogger? logger = null, CancellationToken ct = default)
        {
            if (ddb == null) throw new ArgumentNullException(nameof(ddb));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));

            try
            {
                // Check if table exists
                try
                {
                    var desc = await ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                    logger?.LogInformation("DynamoDB table {Table} already exists (status: {Status})", tableName, desc.Table.TableStatus);
                    // Optionally ensure TTL is enabled; attempt to enable if not
                    await EnsureTtlEnabledAsync(ddb, tableName, logger, ct).ConfigureAwait(false);
                    return true;
                }
                catch (ResourceNotFoundException)
                {
                    logger?.LogInformation("DynamoDB table {Table} not found; creating", tableName);
                }

                // Define attribute definitions and key schema
                var createReq = new CreateTableRequest
                {
                    TableName = tableName,
                    AttributeDefinitions = new List<AttributeDefinition>
                    {
                        new AttributeDefinition { AttributeName = "id", AttributeType = "S" },
                        new AttributeDefinition { AttributeName = "type", AttributeType = "S" },
                        new AttributeDefinition { AttributeName = "channel", AttributeType = "S" },
                        new AttributeDefinition { AttributeName = "createdAtUtc", AttributeType = "N" }
                    },
                    KeySchema = new List<KeySchemaElement>
                    {
                        new KeySchemaElement { AttributeName = "id", KeyType = "HASH" } // PK
                    },
                    // Two GSIs: by type+createdAtUtc and by channel+createdAtUtc
                    GlobalSecondaryIndexes = new List<GlobalSecondaryIndex>
                    {
                        new GlobalSecondaryIndex
                        {
                            IndexName = "TypeCreatedAtIndex",
                            KeySchema = new List<KeySchemaElement>
                            {
                                new KeySchemaElement { AttributeName = "type", KeyType = "HASH" },
                                new KeySchemaElement { AttributeName = "createdAtUtc", KeyType = "RANGE" }
                            },
                            Projection = new Projection { ProjectionType = "ALL" },
                            ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 5 }
                        },
                        new GlobalSecondaryIndex
                        {
                            IndexName = "ChannelCreatedAtIndex",
                            KeySchema = new List<KeySchemaElement>
                            {
                                new KeySchemaElement { AttributeName = "channel", KeyType = "HASH" },
                                new KeySchemaElement { AttributeName = "createdAtUtc", KeyType = "RANGE" }
                            },
                            Projection = new Projection { ProjectionType = "ALL" },
                            ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 5 }
                        }
                    },
                    // Use modest provisioned throughput by default; callers can change to On-Demand by modifying this request.
                    ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 5 }
                };

                var createResp = await ddb.CreateTableAsync(createReq, ct).ConfigureAwait(false);
                logger?.LogInformation("CreateTable initiated for {Table}. Waiting for ACTIVE status...", tableName);

                // Wait until table is ACTIVE
                var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var timeout = TimeSpan.FromMinutes(5);
                waitCts.CancelAfter(timeout);

                while (!waitCts.IsCancellationRequested)
                {
                    var desc = await ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                    if (string.Equals(desc.Table.TableStatus, TableStatus.ACTIVE.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        logger?.LogInformation("DynamoDB table {Table} is ACTIVE", tableName);
                        break;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
                }

                // Enable TTL on attribute "ttl"
                await EnsureTtlEnabledAsync(ddb, tableName, logger, ct).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to create or verify DynamoDB table {Table}", tableName);
                throw;
            }
        }

        private static async Task EnsureTtlEnabledAsync(IAmazonDynamoDB ddb, string tableName, ILogger? logger, CancellationToken ct)
        {
            try
            {
                var ttlDesc = await ddb.DescribeTimeToLiveAsync(new DescribeTimeToLiveRequest { TableName = tableName }, ct).ConfigureAwait(false);
                if (ttlDesc.TimeToLiveDescription != null &&
                    string.Equals(ttlDesc.TimeToLiveDescription.TimeToLiveStatus, TimeToLiveStatus.ENABLED.Value, StringComparison.OrdinalIgnoreCase))
                {
                    logger?.LogDebug("TTL already enabled on table {Table} (attribute: {Attr})", tableName, ttlDesc.TimeToLiveDescription.AttributeName);
                    return;
                }

                var updateReq = new UpdateTimeToLiveRequest
                {
                    TableName = tableName,
                    TimeToLiveSpecification = new TimeToLiveSpecification
                    {
                        AttributeName = "ttl",
                        Enabled = true
                    }
                };

                var updateResp = await ddb.UpdateTimeToLiveAsync(updateReq, ct).ConfigureAwait(false);
                logger?.LogInformation("Requested TTL enable on table {Table} for attribute 'ttl'", tableName);
            }
            catch (Exception ex)
            {
                // TTL enabling can fail if not supported or permissions missing; log and continue
                logger?.LogWarning(ex, "Could not ensure TTL enabled on table {Table}", tableName);
            }
        }
    }
}
