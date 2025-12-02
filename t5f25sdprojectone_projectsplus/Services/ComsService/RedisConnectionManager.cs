// src/ProjectsPlus.Comms/Realtime/RedisConnectionManager.cs
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using IDatabase = StackExchange.Redis.IDatabase;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class RedisConnectionManagerOptions
    {
        public string RedisConfiguration { get; init; } = "localhost:6379";
        public string InstanceId { get; init; } = Environment.MachineName;
        public string Prefix { get; init; } = "comms:";
        public int PubSubDatabase { get; init; } = 0;
    }

    internal sealed class PubSubMessage
    {
        public string Target { get; set; } = string.Empty; // connectionId | user:{guid} | room:{id} | broadcast
        public RealtimeEnvelope Envelope { get; set; } = new RealtimeEnvelope();
    }

    public class RedisConnectionManager : IConnectionManager, IDisposable
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ISubscriber _sub;
        private readonly RedisConnectionManagerOptions _opts;
        private readonly ILogger<RedisConnectionManager> _logger;

        // Local in-memory sockets for this instance only
        private readonly ConcurrentDictionary<string, WebSocket> _localSockets = new();
        private readonly ConcurrentDictionary<string, PresenceState> _localPresence = new();

        // userId -> set of connectionIds (stored in Redis as a Redis Set)
        // connectionId -> instanceId mapping (Redis string)
        // presence stored as Redis hash per connection

        public event Func<string, Guid?, PresenceState, Task>? OnConnected;
        public event Func<string, Guid?, Task>? OnDisconnected;

        public RedisConnectionManager(RedisConnectionManagerOptions opts, ILogger<RedisConnectionManager> logger)
        {
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _redis = ConnectionMultiplexer.Connect(_opts.RedisConfiguration);
            _db = _redis.GetDatabase();
            _sub = _redis.GetSubscriber();

            // subscribe to instance channel for cross-instance messages
            var channel = $"{_opts.Prefix}pubsub";
            _sub.Subscribe(channel, (ch, msg) => HandlePubSubMessage(msg));
        }

        private void HandlePubSubMessage(RedisValue msg)
        {
            try
            {
                var ps = JsonSerializer.Deserialize<PubSubMessage>(msg)!;
                if (ps == null) return;

                // If target is a connectionId and it's local, send directly
                if (!string.IsNullOrWhiteSpace(ps.Target))
                {
                    if (ps.Target.StartsWith("connection:"))
                    {
                        var conn = ps.Target.Substring("connection:".Length);
                        if (_localSockets.TryGetValue(conn, out var socket) && socket.State == WebSocketState.Open)
                        {
                            _ = SendToConnectionAsync(conn, ps.Envelope);
                        }
                        return;
                    }

                    if (ps.Target.StartsWith("user:"))
                    {
                        var guidText = ps.Target.Substring("user:".Length);
                        if (Guid.TryParse(guidText, out var userId))
                        {
                            _ = SendToUserAsync(userId, ps.Envelope);
                        }
                        return;
                    }

                    if (ps.Target.StartsWith("room:"))
                    {
                        // room broadcast: ChatroomService should publish per-instance or resolve members
                        // fallback: broadcast to all local sockets
                        var tasks = _localSockets.Keys.Select(c => SendToConnectionAsync(c, ps.Envelope));
                        _ = Task.WhenAll(tasks);
                        return;
                    }

                    if (ps.Target == "broadcast")
                    {
                        var tasks = _localSockets.Keys.Select(c => SendToConnectionAsync(c, ps.Envelope));
                        _ = Task.WhenAll(tasks);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle pubsub message");
            }
        }

        private string RedisKey(string key) => $"{_opts.Prefix}{key}";

        public async Task RegisterAsync(string connectionId, Guid? userId, WebSocket socket)
        {
            _localSockets[connectionId] = socket;

            // map connection -> instance
            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:instance"), _opts.InstanceId).ConfigureAwait(false);

            // add connection to user set if userId present
            if (userId.HasValue)
            {
                await _db.SetAddAsync(RedisKey($"user:{userId.Value}:conns"), connectionId).ConfigureAwait(false);
                await _db.StringSetAsync(RedisKey($"conn:{connectionId}:user"), userId.Value.ToString("D")).ConfigureAwait(false);
            }

            // presence
            var presence = new PresenceState { Status = "online", LastSeenUtc = DateTime.UtcNow };
            _localPresence[connectionId] = presence;
            var presenceJson = JsonSerializer.Serialize(presence);
            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:presence"), presenceJson).ConfigureAwait(false);

            if (OnConnected != null)
            {
                try { await OnConnected.Invoke(connectionId, userId, presence).ConfigureAwait(false); } catch { }
            }
        }

        public async Task UnregisterAsync(string connectionId)
        {
            _localSockets.TryRemove(connectionId, out var _);
            _localPresence.TryRemove(connectionId, out var _);

            // remove mapping and presence
            var instKey = RedisKey($"conn:{connectionId}:instance");
            var userKey = RedisKey($"conn:{connectionId}:user");
            var presenceKey = RedisKey($"conn:{connectionId}:presence");

            var userIdText = await _db.StringGetAsync(userKey).ConfigureAwait(false);
            if (!userIdText.IsNullOrEmpty)
            {
                if (Guid.TryParse(userIdText, out var userId))
                {
                    await _db.SetRemoveAsync(RedisKey($"user:{userId}:conns"), connectionId).ConfigureAwait(false);
                }
            }

            await _db.KeyDeleteAsync(new RedisKey[] { instKey, userKey, presenceKey }).ConfigureAwait(false);

            if (OnDisconnected != null)
            {
                try { await OnDisconnected.Invoke(connectionId, userIdText.IsNullOrEmpty ? (Guid?)null : Guid.Parse(userIdText)).ConfigureAwait(false); } catch { }
            }
        }

        public bool TryGetSocket(string connectionId, out WebSocket? socket)
        {
            if (_localSockets.TryGetValue(connectionId, out var s))
            {
                socket = s;
                return true;
            }
            socket = null;
            return false;
        }

        public IReadOnlyList<string> GetConnectionsForUser(Guid userId)
        {
            // read from Redis set (synchronous wrapper)
            var members = _db.SetMembers(RedisKey($"user:{userId}:conns"));
            return members.Select(m => (string)m).ToArray();
        }

        public async Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // If connection is local, send directly
            if (_localSockets.TryGetValue(connectionId, out var socket) && socket.State == WebSocketState.Open)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                return;
            }

            // Otherwise publish to pubsub so the owning instance can deliver
            var msg = new PubSubMessage { Target = $"connection:{connectionId}", Envelope = envelope };
            var channel = $"{_opts.Prefix}pubsub";
            var payload = JsonSerializer.Serialize(msg);
            await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
        }

        public async Task SendToUserAsync(Guid userId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // get connections for user (may include remote connections)
            var members = _db.SetMembers(RedisKey($"user:{userId}:conns"));
            foreach (var m in members)
            {
                var connId = (string)m;
                // attempt local send; if not local, publish
                if (_localSockets.TryGetValue(connId, out var socket) && socket.State == WebSocketState.Open)
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                }
                else
                {
                    var msg = new PubSubMessage { Target = $"connection:{connId}", Envelope = envelope };
                    var channel = $"{_opts.Prefix}pubsub";
                    var payload = JsonSerializer.Serialize(msg);
                    await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
                }
            }
        }

        public async Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // Room membership is typically stored in ChatroomService; if ChatroomService stores membership in Redis,
            // you can fetch members and publish per-user. Fallback: publish broadcast to all instances.
            var msg = new PubSubMessage { Target = $"room:{roomId}", Envelope = envelope };
            var channel = $"{_opts.Prefix}pubsub";
            var payload = JsonSerializer.Serialize(msg);
            await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
        }

        public async Task SetPresenceAsync(string connectionId, PresenceState state)
        {
            state.LastSeenUtc = DateTime.UtcNow;
            _localPresence[connectionId] = state;
            var presenceJson = JsonSerializer.Serialize(state);
            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:presence"), presenceJson).ConfigureAwait(false);
        }

        public async Task<PresenceState?> GetPresenceAsync(string connectionId)
        {
            if (_localPresence.TryGetValue(connectionId, out var s)) return s;
            var json = await _db.StringGetAsync(RedisKey($"conn:{connectionId}:presence")).ConfigureAwait(false);
            if (json.IsNullOrEmpty) return null;
            try { return JsonSerializer.Deserialize<PresenceState>(json!); } catch { return null; }
        }

        public void Dispose()
        {
            try { _redis?.Dispose(); } catch { }
        }
    }
}
