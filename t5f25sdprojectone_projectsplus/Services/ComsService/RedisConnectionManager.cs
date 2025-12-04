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
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;
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
        public string Target { get; set; } = string.Empty; // connection:..., user:..., room:..., broadcast
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

        public event Func<string, long?, PresenceState, Task>? OnConnected;
        public event Func<string, long?, Task>? OnDisconnected;

        public RedisConnectionManager(RedisConnectionManagerOptions opts, ILogger<RedisConnectionManager> logger)
        {
            _opts = opts ?? throw new ArgumentNullException(nameof(opts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _redis = ConnectionMultiplexer.Connect(_opts.RedisConfiguration);
            _db = _redis.GetDatabase();
            _sub = _redis.GetSubscriber();

            var channel = $"{_opts.Prefix}pubsub";
            _sub.Subscribe(channel, (ch, msg) => HandlePubSubMessage(msg));
        }

        private void HandlePubSubMessage(RedisValue msg)
        {
            try
            {
                var ps = JsonSerializer.Deserialize<PubSubMessage>(msg)!;
                if (ps == null) return;

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
                        var idText = ps.Target.Substring("user:".Length);
                        if (long.TryParse(idText, out long userId))
                        {
                            _ = BroadcastToUserAsync(userId, ps.Envelope);
                        }
                        return;
                    }

                    if (ps.Target.StartsWith("room:") || ps.Target == "broadcast")
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

        // RegisterAsync signature required by IConnectionManager (no CancellationToken)
        public async Task RegisterAsync(string connectionId, long? userId, WebSocket socket)
        {
            _localSockets[connectionId] = socket;

            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:instance"), _opts.InstanceId).ConfigureAwait(false);

            if (userId.HasValue)
            {
                await _db.SetAddAsync(RedisKey($"user:{userId.Value}:conns"), connectionId).ConfigureAwait(false);
                await _db.StringSetAsync(RedisKey($"conn:{connectionId}:user"), userId.Value.ToString("D")).ConfigureAwait(false);
            }

            var presence = new PresenceState { Status = "online", LastSeenUtc = DateTime.UtcNow };
            _localPresence[connectionId] = presence;
            var presenceJson = JsonSerializer.Serialize(presence);
            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:presence"), presenceJson).ConfigureAwait(false);

            if (OnConnected != null)
            {
                try { await OnConnected.Invoke(connectionId, userId, presence).ConfigureAwait(false); } catch { }
            }
        }

        // UnregisterAsync with CancellationToken
        public async Task UnregisterAsync(string connectionId, CancellationToken ct = default)
        {
            _localSockets.TryRemove(connectionId, out var _);
            _localPresence.TryRemove(connectionId, out var _);

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
                try { if (userIdText.IsNullOrEmpty) { await OnDisconnected.Invoke(connectionId, null).ConfigureAwait(false); } else { await OnDisconnected.Invoke(connectionId, (long?)userIdText).ConfigureAwait(false); } } catch { }
            }
        }

        // TryGetSocket with CancellationToken (token not used for in-memory lookup)
        public bool TryGetSocket(string connectionId, out WebSocket? socket, CancellationToken ct = default)
        {
            if (_localSockets.TryGetValue(connectionId, out var s))
            {
                socket = s;
                return true;
            }
            socket = null;
            return false;
        }

        // GetConnectionsForUser with CancellationToken
        public IReadOnlyList<string> GetConnectionsForUser(long userId, CancellationToken ct = default)
        {
            var members = _db.SetMembers(RedisKey($"user:{userId}:conns"));
            return members.Select(m => (string)m).ToArray();
        }

        // Send to a specific connection (local or publish)
        public async Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            if (_localSockets.TryGetValue(connectionId, out var socket) && socket.State == WebSocketState.Open)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                return;
            }

            var msg = new PubSubMessage { Target = $"connection:{connectionId}", Envelope = envelope };
            var channel = $"{_opts.Prefix}pubsub";
            var payload = JsonSerializer.Serialize(msg);
            await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
        }

        // SendToUserAsync delegates to BroadcastToUserAsync
        public Task SendToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default)
            => BroadcastToUserAsync(userId, envelope, ct);

        // BroadcastToUserAsync (Guid) - publish or send to local sockets
        public async Task BroadcastToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            var members = _db.SetMembers(RedisKey($"user:{userId}:conns"));
            foreach (var m in members)
            {
                var connId = (string)m;
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

        // BroadcastToRoomAsync(string) required by interface
        public async Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            if (Guid.TryParse(roomId, out var rg))
            {
                await BroadcastToRoomAsync(rg, envelope, ct).ConfigureAwait(false);
                return;
            }

            var msg = new PubSubMessage { Target = $"room:{roomId}", Envelope = envelope };
            var channel = $"{_opts.Prefix}pubsub";
            var payload = JsonSerializer.Serialize(msg);
            await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
        }

        // BroadcastToRoomAsync(Guid) convenience overload
        public async Task BroadcastToRoomAsync(Guid roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            var msg = new PubSubMessage { Target = $"room:{roomId}", Envelope = envelope };
            var channel = $"{_opts.Prefix}pubsub";
            var payload = JsonSerializer.Serialize(msg);
            await _sub.PublishAsync(channel, payload).ConfigureAwait(false);
        }

        // SetPresenceAsync with CancellationToken
        public async Task SetPresenceAsync(string connectionId, PresenceState state, CancellationToken ct = default)
        {
            state.LastSeenUtc = DateTime.UtcNow;
            _localPresence[connectionId] = state;
            var presenceJson = JsonSerializer.Serialize(state);
            await _db.StringSetAsync(RedisKey($"conn:{connectionId}:presence"), presenceJson).ConfigureAwait(false);
        }

        // GetPresenceAsync with CancellationToken
        public async Task<PresenceState?> GetPresenceAsync(string connectionId, CancellationToken ct = default)
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
