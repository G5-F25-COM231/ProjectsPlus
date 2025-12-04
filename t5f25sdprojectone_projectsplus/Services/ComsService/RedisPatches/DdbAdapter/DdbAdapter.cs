// src/Infrastructure/DdbAdapter.cs
using System.Collections.Concurrent;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// DynamoDB-backed adapter implementing IRedisClient.
    /// Assumptions:
    /// - A DynamoDB table exists with PartitionKey = "Channel" (string) and SortKey = "CreatedAtUtc" (number)
    ///   for pub/sub messages (table name provided to constructor).
    /// - For simple key/value and sets we use a single table pattern where items are stored with PK = ID
    ///   and attribute "Type" distinguishes item kinds (e.g., "kv", "set", "conn", "ws", etc.).
    /// - This adapter provides durable writes (PutItem/UpdateItem) and a best-effort pub/sub via a poller.
    /// Notes:
    /// - For production, tune provisioned capacity, use Dynamo Streams for lower-latency pub/sub, and add retries/backoff.
    /// - This implementation focuses on correctness and parity with the IRedisClient surface.
    /// </summary>
    public sealed class DdbAdapter : IRedisClient
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly string _tableName;
        private readonly ILogger<DdbAdapter>? _logger;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Action<string>, byte>> _localSubs = new();
        private readonly ConcurrentDictionary<string, long> _channelLastSeenTicks = new();
        private readonly TimeSpan _pollInterval;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pollerTask;
        private bool _disposed;
        private static readonly byte _marker = 0;

        private readonly IServiceProvider _services;

        /// <summary>
        /// Create a DdbAdapter.
        /// - ddb: IAmazonDynamoDB client (configured with region/credentials).
        /// - tableName: DynamoDB table used for pub/sub and key/set storage.
        /// - pollInterval: how often to poll pubsub messages for subscribed channels.
        /// </summary>
        //public DdbAdapter(IAmazonDynamoDB ddb, string tableName, ILogger<DdbAdapter>? logger = null, TimeSpan? pollInterval = null)
        public DdbAdapter(IServiceProvider services, TimeSpan? pollInterval = null)
        {
            _services = services;
            using var scope = _services.CreateScope();
            var ddb = scope.ServiceProvider.GetRequiredService<DynamodbService>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DdbAdapter>>();

            _ddb = ddb.DdbClient ?? throw new ArgumentNullException(nameof(ddb));
            _tableName = ddb.Options.TableName ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger;
            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
            _pollerTask = Task.Run(PollerLoopAsync);
        }

        #region Strings (KV)

        public async Task<bool> StringSetAsync(string key, string value, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));
            var item = new Dictionary<string, AttributeValue>
            {
                ["ID"] = new AttributeValue { S = key },
                ["Type"] = new AttributeValue { S = "kv" },
                ["Value"] = new AttributeValue { S = value ?? string.Empty },
                ["UpdatedAtUtc"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
            };

            var req = new PutItemRequest
            {
                TableName = _tableName,
                Item = item
            };

            await _ddb.PutItemAsync(req, ct).ConfigureAwait(false);
            return true;
        }

        public async Task<string?> StringGetAsync(string key, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));
            var req = new GetItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = key } },
                ConsistentRead = true
            };

            var resp = await _ddb.GetItemAsync(req, ct).ConfigureAwait(false);
            if (resp.Item == null || resp.Item.Count == 0) return null;
            if (resp.Item.TryGetValue("Value", out var v) && v.S != null) return v.S;
            return null;
        }

        public async Task<bool> KeyDeleteAsync(string key, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));
            var req = new DeleteItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = key } }
            };

            await _ddb.DeleteItemAsync(req, ct).ConfigureAwait(false);
            return true;
        }

        #endregion

        #region Sets

        // Sets are stored as items with ID = "set:{setKey}" and attribute "Members" as String Set (SS).
        public async Task<long> SetAddAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            if (member == null) throw new ArgumentNullException(nameof(member));

            var id = $"set:{setKey}";
            // Use UpdateItem to add to SS atomically
            var req = new UpdateItemRequest
            {
                TableName = _tableName,
                Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = id } },
                ExpressionAttributeNames = new Dictionary<string, string> { ["#T"] = "Type", ["#M"] = "Members" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":t"] = new AttributeValue { S = "set" },
                    [":m"] = new AttributeValue { SS = new List<string> { member } }
                },
                UpdateExpression = "SET #T = if_not_exists(#T, :t), #M = list_append(if_not_exists(#M, :empty), :m)",
                // Note: DynamoDB does not support set union in UpdateExpression easily; using list_append as a simple approach,
                // but to keep true set semantics we will read-modify-write below if necessary.
                // Simpler and correct approach: read existing SS and add member if missing via conditional Put/Update.
            };

            // Safer approach: read current members, then PutItem with merged SS
            var getReq = new GetItemRequest { TableName = _tableName, Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = id } }, ConsistentRead = true };
            var getResp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            HashSet<string> members = new();
            if (getResp.Item != null && getResp.Item.TryGetValue("Members", out var mAttr) && mAttr.SS != null)
            {
                foreach (var s in mAttr.SS) members.Add(s);
            }

            var added = members.Add(member) ? 1L : 0L;
            var putItem = new Dictionary<string, AttributeValue>
            {
                ["ID"] = new AttributeValue { S = id },
                ["Type"] = new AttributeValue { S = "set" },
                ["Members"] = new AttributeValue { SS = members.ToList() },
                ["UpdatedAtUtc"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
            };

            var putReq = new PutItemRequest { TableName = _tableName, Item = putItem };
            await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);
            return added;
        }

        public async Task<long> SetRemoveAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            if (member == null) throw new ArgumentNullException(nameof(member));

            var id = $"set:{setKey}";
            var getReq = new GetItemRequest { TableName = _tableName, Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = id } }, ConsistentRead = true };
            var getResp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (getResp.Item == null || !getResp.Item.TryGetValue("Members", out var mAttr) || mAttr.SS == null) return 0L;

            var members = new HashSet<string>(mAttr.SS);
            var removed = members.Remove(member) ? 1L : 0L;

            if (members.Count == 0)
            {
                // remove the item
                var delReq = new DeleteItemRequest { TableName = _tableName, Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = id } } };
                await _ddb.DeleteItemAsync(delReq, ct).ConfigureAwait(false);
            }
            else
            {
                var putItem = new Dictionary<string, AttributeValue>
                {
                    ["ID"] = new AttributeValue { S = id },
                    ["Type"] = new AttributeValue { S = "set" },
                    ["Members"] = new AttributeValue { SS = members.ToList() },
                    ["UpdatedAtUtc"] = new AttributeValue { N = DateTime.UtcNow.Ticks.ToString() }
                };
                var putReq = new PutItemRequest { TableName = _tableName, Item = putItem };
                await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);
            }

            return removed;
        }

        public async Task<string[]> SetMembersAsync(string setKey, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            var id = $"set:{setKey}";
            var getReq = new GetItemRequest { TableName = _tableName, Key = new Dictionary<string, AttributeValue> { ["ID"] = new AttributeValue { S = id } }, ConsistentRead = true };
            var getResp = await _ddb.GetItemAsync(getReq, ct).ConfigureAwait(false);
            if (getResp.Item == null || !getResp.Item.TryGetValue("Members", out var mAttr) || mAttr.SS == null) return Array.Empty<string>();
            return mAttr.SS.ToArray();
        }

        public async Task<bool> SetContainsAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            if (member == null) throw new ArgumentNullException(nameof(member));
            var members = await SetMembersAsync(setKey, ct).ConfigureAwait(false);
            return members.Contains(member);
        }

        #endregion

        #region Pub/Sub

        /// <summary>
        /// Publish: write a row to the PubSub table (PartitionKey = Channel, SortKey = CreatedAtUtc ticks).
        /// Also invoke local subscribers immediately (best-effort).
        /// Returns number of local subscribers invoked (best-effort).
        /// </summary>
        public async Task<long> PublishAsync(string channel, string message, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (message == null) throw new ArgumentNullException(nameof(message));

            // Create item with Channel as PK and CreatedAtUtc as sort key (number)
            var createdTicks = DateTime.UtcNow.Ticks;
            var item = new Dictionary<string, AttributeValue>
            {
                ["Channel"] = new AttributeValue { S = channel },
                ["CreatedAtUtc"] = new AttributeValue { N = createdTicks.ToString() },
                ["MessageId"] = new AttributeValue { S = Guid.NewGuid().ToString("D") },
                ["Payload"] = new AttributeValue { S = message }
            };

            var putReq = new PutItemRequest { TableName = _tableName, Item = item };
            await _ddb.PutItemAsync(putReq, ct).ConfigureAwait(false);

            // Invoke local subscribers immediately (best-effort)
            if (_localSubs.TryGetValue(channel, out var handlers))
            {
                var arr = handlers.Keys.ToArray();
                foreach (var h in arr)
                {
                    _ = Task.Run(() =>
                    {
                        try { h(message); } catch (Exception ex) { _logger?.LogDebug(ex, "Local subscriber handler threw"); }
                    }, ct);
                }
                // update last seen so poller doesn't re-deliver same message
                _channelLastSeenTicks.AddOrUpdate(channel, createdTicks, (_, __) => Math.Max(createdTicks, _channelLastSeenTicks[channel]));
                return arr.Length;
            }

            return 0L;
        }

        /// <summary>
        /// Subscribe registers a local handler for a channel. Handlers are invoked asynchronously.
        /// A background poller will also fetch messages published by other instances.
        /// </summary>
        public IDisposable Subscribe(string channel, Action<string> handler)
        {
            ThrowIfDisposed();
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var handlers = _localSubs.GetOrAdd(channel, _ => new ConcurrentDictionary<Action<string>, byte>());
            handlers[handler] = _marker;

            // initialize last seen to now so we don't replay old messages unless desired
            _channelLastSeenTicks.TryAdd(channel, DateTime.UtcNow.Ticks);

            return new Subscription(this, channel, handler);
        }

        private async Task PollerLoopAsync()
        {
            var ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var channels = _localSubs.Keys.ToArray();
                        if (channels.Length > 0)
                        {
                            // For each subscribed channel, query new messages since last seen
                            var tasks = channels.Select(ch => PollChannelAsync(ch, ct)).ToArray();
                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "DdbAdapter poller loop error");
                    }

                    await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on dispose */ }
        }

        private async Task PollChannelAsync(string channel, CancellationToken ct)
        {
            if (!_channelLastSeenTicks.TryGetValue(channel, out var lastSeen)) lastSeen = 0L;

            // Query by partition key (Channel) and sort key > lastSeen
            var req = new QueryRequest
            {
                TableName = _tableName,
                KeyConditionExpression = "Channel = :ch AND CreatedAtUtc > :ts",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":ch"] = new AttributeValue { S = channel },
                    [":ts"] = new AttributeValue { N = lastSeen.ToString() }
                },
                ScanIndexForward = true, // oldest first
                ConsistentRead = false,
                Limit = 100
            };

            QueryResponse resp;
            try
            {
                resp = await _ddb.QueryAsync(req, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "QueryAsync failed for channel {Channel}", channel);
                return;
            }

            if (resp.Items == null || resp.Items.Count == 0) return;

            long maxSeen = lastSeen;
            foreach (var item in resp.Items)
            {
                if (!item.TryGetValue("Payload", out var payloadAttr) || payloadAttr.S == null) continue;
                if (!item.TryGetValue("CreatedAtUtc", out var tsAttr) || tsAttr.N == null) continue;

                if (!long.TryParse(tsAttr.N, out var ts)) ts = DateTime.UtcNow.Ticks;
                var payload = payloadAttr.S;

                // invoke local handlers
                if (_localSubs.TryGetValue(channel, out var handlers))
                {
                    foreach (var h in handlers.Keys.ToArray())
                    {
                        _ = Task.Run(() =>
                        {
                            try { h(payload); } catch (Exception ex) { _logger?.LogDebug(ex, "Subscriber handler threw"); }
                        }, ct);
                    }
                }

                if (ts > maxSeen) maxSeen = ts;
            }

            // update last seen
            _channelLastSeenTicks.AddOrUpdate(channel, maxSeen, (_, __) => Math.Max(maxSeen, _channelLastSeenTicks[channel]));
        }

        #endregion

        #region Subscription helper

        private sealed class Subscription : IDisposable
        {
            private readonly DdbAdapter _parent;
            private readonly string _channel;
            private readonly Action<string> _handler;
            private bool _disposed;

            public Subscription(DdbAdapter parent, string channel, Action<string> handler)
            {
                _parent = parent;
                _channel = channel;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_parent._localSubs.TryGetValue(_channel, out var handlers))
                {
                    handlers.TryRemove(_handler, out _);
                    if (handlers.IsEmpty)
                    {
                        _parent._localSubs.TryRemove(_channel, out _);
                        _parent._channelLastSeenTicks.TryRemove(_channel, out _);
                    }
                }
            }
        }

        #endregion

        #region Lifecycle

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DdbAdapter));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _cts.Cancel();
                _pollerTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch { /* swallow */ }
            finally
            {
                _cts.Dispose();
                _localSubs.Clear();
                _channelLastSeenTicks.Clear();
            }
        }

        #endregion
    }
}
