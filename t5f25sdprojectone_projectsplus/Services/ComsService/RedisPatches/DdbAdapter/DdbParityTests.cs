// tests/Infrastructure.Dynamo.Tests/DdbParityTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Assert = Xunit.Assert;


namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Integration-style parity tests for the Dynamo-backed comms components.
    /// 
    /// These tests are intended to run against a local DynamoDB endpoint (DynamoDB Local or LocalStack).
    /// Configure the endpoint via the environment variable DYNAMO_ENDPOINT (e.g. http://localhost:8000).
    /// If no endpoint is provided the default AWS SDK configuration will be used (useful for CI that provides credentials).
    /// 
    /// The tests create a temporary table per test class run and clean up items they create.
    /// They are not exhaustive but exercise the main behaviors: enqueue/idempotency, registry heartbeats/claims,
    /// connection manager room membership, and idempotency marker lifecycle.
    /// </summary>
    public class DdbParityTests : IAsyncLifetime
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly DdbMessageEnqueuer _enqueuer;
        private readonly DdbIdempotencyStore _idemStore;
        private readonly DdbInstanceRegistry _registry;
        private readonly DdbBackedConnectionManager _connMgr;
        private readonly DdbMessageRetentionCleaner _cleaner;

        public DdbParityTests()
        {
            var endpoint = Environment.GetEnvironmentVariable("DYNAMO_ENDPOINT");
            var config = new AmazonDynamoDBConfig();
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                config.ServiceURL = endpoint;
                // DynamoDB Local ignores region but SDK requires one
                config.RegionEndpoint = RegionEndpoint.USEast1;
            }

            _ddb = new AmazonDynamoDBClient(config);
            _tableName = $"comms_test_{Guid.NewGuid():N}";

            // Create simple instances using null loggers for tests
            //_enqueuer = new DdbMessageEnqueuer( messageRetentionMinutes: 60);
            //_idemStore = new DdbIdempotencyStore(NullLogger<DdbIdempotencyStore>.Instance);
            //_registry = new DdbInstanceRegistry( NullLogger<DdbInstanceRegistry>.Instance);
            //_connMgr = new DdbBackedConnectionManager( NullLogger<DdbBackedConnectionManager>.Instance);
            //_cleaner = new DdbMessageRetentionCleaner(NullLogger<DdbMessageRetentionCleaner>.Instance);
        }

        /// <summary>
        /// Create the table used by the tests. The table schema is minimal:
        /// PK: id (S)
        /// Additional attributes are created on demand by DynamoDB.
        /// </summary>
        public async Task InitializeAsync()
        {
            // Create table with PK = id (S)
            var createReq = new CreateTableRequest
            {
                TableName = _tableName,
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition { AttributeName = "id", AttributeType = "S" }
                },
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = "id", KeyType = "HASH" }
                },
                ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = 5, WriteCapacityUnits = 5 }
            };

            try
            {
                await _ddb.CreateTableAsync(createReq).ConfigureAwait(false);

                // Wait until ACTIVE
                for (var i = 0; i < 30; i++)
                {
                    var desc = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = _tableName }).ConfigureAwait(false);
                    if (desc.Table.TableStatus == TableStatus.ACTIVE) break;
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
            catch (ResourceInUseException)
            {
                // table already exists (unlikely with GUID name) - ignore
            }
        }

        public async Task DisposeAsync()
        {
            // Attempt to delete the table
            try
            {
                await _ddb.DeleteTableAsync(new DeleteTableRequest { TableName = _tableName }).ConfigureAwait(false);
            }
            catch
            {
                // best-effort cleanup; ignore errors
            }

            _ddb.Dispose();
            _enqueuer.Dispose();
            _idemStore.Dispose();
            _connMgr.Dispose();
            _cleaner.Dispose();
        }

        [Fact(DisplayName = "Enqueue with idempotency prevents duplicate messages")]
        public async Task Enqueue_Idempotency_PreventsDuplicates()
        {
            var idempotencyKey = $"key-{Guid.NewGuid():N}";
            var targetType = "connection";
            var targetId = "conn-1";
            var payload = "{\"hello\":\"world\"}";

            // First enqueue should create a new message id
            var msgId1 = await _enqueuer.EnqueueAsync(targetType, targetId, payload, idempotencyKey).ConfigureAwait(false);
            msgId1.Should().NotBeNullOrWhiteSpace();

            // Second enqueue with same idempotency key should return the same message id
            var msgId2 = await _enqueuer.EnqueueAsync(targetType, targetId, payload, idempotencyKey).ConfigureAwait(false);
            msgId2.Should().Be(msgId1);

            // Verify marker exists in table
            var getReq = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = $"idem:{idempotencyKey}" } }
            };
            var getResp = await _ddb.GetItemAsync(getReq).ConfigureAwait(false);
            getResp.Item.Should().ContainKey("messageId");
            getResp.Item["messageId"].S.Should().Be(msgId1);
        }

        [Fact(DisplayName = "Idempotency store create/get/remove lifecycle")]
        public async Task IdempotencyStore_CreateGetRemove()
        {
            var key = $"idem-{Guid.NewGuid():N}";
            var messageId = $"message:{Guid.NewGuid():D}";
            var ttl = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();

            var created = await _idemStore.TryCreateMarkerAsync(key, messageId, ttl).ConfigureAwait(false);
            created.Should().BeTrue();

            var fetched = await _idemStore.GetMessageIdForKeyAsync(key).ConfigureAwait(false);
            fetched.Should().Be(messageId);

            var removed = await _idemStore.RemoveMarkerAsync(key).ConfigureAwait(false);
            removed.Should().BeTrue();

            var after = await _idemStore.GetMessageIdForKeyAsync(key).ConfigureAwait(false);
            after.Should().BeNull();
        }

        [Fact(DisplayName = "Instance registry register, heartbeat, claim/release connection")]
        public async Task Registry_RegisterHeartbeatClaimRelease()
        {
            var instanceId = $"inst-{Guid.NewGuid():N}";
            var instanceInfo = new IInstanceRegistry.InstanceInfo(instanceId, "http://localhost", DateTime.UtcNow, new Dictionary<string, string> { ["zone"] = "test" });

            await _registry.RegisterAsync(instanceInfo).ConfigureAwait(false);

            var fetched = await _registry.GetInstanceAsync(instanceId).ConfigureAwait(false);
            fetched.Should().NotBeNull();
            fetched!.InstanceId.Should().Be(instanceId);
            fetched.Address.Should().Be("http://localhost");

            var hb = await _registry.HeartbeatAsync(instanceId).ConfigureAwait(false);
            hb.Should().BeTrue();

            // Create a connection item directly via connection manager
            var connId = $"conn-{Guid.NewGuid():N}";
            await _connMgr.CreateOrUpdateConnectionAsync(connId, userId: "user1", ownerInstance: null).ConfigureAwait(false);

            // Try claim connection
            var claimed = await _registry.TryClaimConnectionAsync(connId, instanceId).ConfigureAwait(false);
            claimed.Should().BeTrue();

            var owner = await _registry.GetOwnerForConnectionAsync(connId).ConfigureAwait(false);
            owner.Should().Be(instanceId);

            // Release
            var released = await _registry.ReleaseConnectionAsync(connId, instanceId).ConfigureAwait(false);
            released.Should().BeTrue();

            var ownerAfter = await _registry.GetOwnerForConnectionAsync(connId).ConfigureAwait(false);
            ownerAfter.Should().BeNull();
        }

        [Fact(DisplayName = "Connection manager add/remove rooms and list connections in room")]
        public async Task ConnectionManager_RoomMembership()
        {
            var connA = $"conn-{Guid.NewGuid():N}";
            var connB = $"conn-{Guid.NewGuid():N}";
            var room = $"room-{Guid.NewGuid():N}";

            await _connMgr.CreateOrUpdateConnectionAsync(connA, userId: "uA").ConfigureAwait(false);
            await _connMgr.CreateOrUpdateConnectionAsync(connB, userId: "uB").ConfigureAwait(false);

            // Add both to room
            await _connMgr.AddConnectionToRoomAsync(connA, room).ConfigureAwait(false);
            await _connMgr.AddConnectionToRoomAsync(connB, room).ConfigureAwait(false);

            var members = await _connMgr.ListConnectionsInRoomAsync(room).ConfigureAwait(false);
            members.Should().Contain(new[] { connA, connB });

            // Remove one
            await _connMgr.RemoveConnectionFromRoomAsync(connA, room).ConfigureAwait(false);
            var membersAfter = await _connMgr.ListConnectionsInRoomAsync(room).ConfigureAwait(false);
            membersAfter.Should().Contain(connB).And.NotContain(connA);
        }

        [Fact(DisplayName = "Retention cleaner deletes items with expired ttl or old delivered/failed timestamps")]
        public async Task RetentionCleaner_RemovesOldMessages()
        {
            // Insert three message items:
            // 1) expired via ttl
            // 2) deliveredAtUtc older than cutoff
            // 3) failedAtUtc older than cutoff
            var now = DateTimeOffset.UtcNow;
            var expiredMsg = $"message:{Guid.NewGuid():D}";
            var deliveredMsg = $"message:{Guid.NewGuid():D}";
            var failedMsg = $"message:{Guid.NewGuid():D}";

            var items = new[]
            {
                new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = expiredMsg },
                    ["type"] = new AttributeValue { S = "message" },
                    ["ttl"] = new AttributeValue { N = now.AddSeconds(-10).ToUnixTimeSeconds().ToString() } // already expired
                },
                new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = deliveredMsg },
                    ["type"] = new AttributeValue { S = "message" },
                    ["deliveredAtUtc"] = new AttributeValue { N = DateTime.UtcNow.Subtract(TimeSpan.FromDays(10)).Ticks.ToString() }
                },
                new Dictionary<string, AttributeValue>
                {
                    ["id"] = new AttributeValue { S = failedMsg },
                    ["type"] = new AttributeValue { S = "message" },
                    ["failedAtUtc"] = new AttributeValue { N = DateTime.UtcNow.Subtract(TimeSpan.FromDays(10)).Ticks.ToString() }
                }
            };

            foreach (var it in items)
            {
                await _ddb.PutItemAsync(new PutItemRequest { TableName = _tableName, Item = it }).ConfigureAwait(false);
            }

            // Run cleaner with retention of 7 days -> should remove delivered/failed older than 7 days and ttl expired
            var removed = await _cleaner.CleanupAsync(TimeSpan.FromDays(7), pageSize: 50).ConfigureAwait(false);
            //removed.Should().BeGreaterOrEqualTo(3);
            Assert.True(removed >= 3);

            // Verify items are gone
            async Task<bool> ExistsAsync(string id)
            {
                var resp = await _ddb.GetItemAsync(new GetItemRequest { TableName = _tableName, Key = new Dictionary<string, AttributeValue> { ["id"] = new AttributeValue { S = id } } }).ConfigureAwait(false);
                return resp.Item != null && resp.Item.Count > 0;
            }

            (await ExistsAsync(expiredMsg).ConfigureAwait(false)).Should().BeFalse();
            (await ExistsAsync(deliveredMsg).ConfigureAwait(false)).Should().BeFalse();
            (await ExistsAsync(failedMsg).ConfigureAwait(false)).Should().BeFalse();
        }
    }
}
