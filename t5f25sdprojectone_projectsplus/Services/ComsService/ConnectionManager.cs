// src/ProjectsPlus.Comms/Realtime/ConnectionManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class ConnectionInfo
    {
        public string ConnectionId { get; init; } = string.Empty;
        public Guid? UserId { get; set; }
        public WebSocket Socket { get; init; } = default!;
        public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    }

    public sealed class PresenceState
    {
        public string? Status { get; set; } // e.g., "online", "away", "dnd"
        public IDictionary<string, object?>? Meta { get; set; }
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    public class ConnectionManager : IConnectionManager
    {
        private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
        private readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> _userIndex = new();
        private readonly ConcurrentDictionary<string, PresenceState> _presence = new();

        public event Func<string, Guid?, PresenceState, Task>? OnConnected;
        public event Func<string, Guid?, Task>? OnDisconnected;

        public Task RegisterAsync(string connectionId, Guid? userId, WebSocket socket)
        {
            var info = new ConnectionInfo { ConnectionId = connectionId, UserId = userId, Socket = socket, ConnectedAt = DateTime.UtcNow };
            _connections[connectionId] = info;

            if (userId.HasValue)
            {
                var bag = _userIndex.GetOrAdd(userId.Value, _ => new ConcurrentBag<string>());
                bag.Add(connectionId);
            }

            var presence = new PresenceState { Status = "online", LastSeenUtc = DateTime.UtcNow };
            _presence[connectionId] = presence;

            return OnConnected?.Invoke(connectionId, userId, presence) ?? Task.CompletedTask;
        }

        public Task UnregisterAsync(string connectionId)
        {
            if (_connections.TryRemove(connectionId, out var info))
            {
                if (info.UserId.HasValue && _userIndex.TryGetValue(info.UserId.Value, out var bag))
                {
                    // best-effort removal: rebuild bag without the connectionId
                    var remaining = new ConcurrentBag<string>(bag.Where(c => c != connectionId));
                    _userIndex[info.UserId.Value] = remaining;
                }

                _presence.TryRemove(connectionId, out _);
                return OnDisconnected?.Invoke(connectionId, info.UserId) ?? Task.CompletedTask;
            }
            return Task.CompletedTask;
        }

        public bool TryGetSocket(string connectionId, out WebSocket? socket)
        {
            socket = null;
            if (_connections.TryGetValue(connectionId, out var info))
            {
                socket = info.Socket;
                return true;
            }
            return false;
        }

        public IReadOnlyList<string> GetConnectionsForUser(Guid userId)
        {
            if (_userIndex.TryGetValue(userId, out var bag))
                return bag.ToArray();
            return Array.Empty<string>();
        }

        public async Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            if (!TryGetSocket(connectionId, out var socket) || socket == null || socket.State != WebSocketState.Open) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        public async Task SendToUserAsync(Guid userId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            var conns = GetConnectionsForUser(userId);
            var tasks = conns.Select(c => SendToConnectionAsync(c, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public async Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // Room membership is external (ChatroomService). This method is a convenience that expects
            // the caller to resolve members and call SendToUserAsync or SendToConnectionAsync.
            // For now, broadcast to all connections (use with caution).
            var tasks = _connections.Keys.Select(c => SendToConnectionAsync(c, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public Task SetPresenceAsync(string connectionId, PresenceState state)
        {
            state.LastSeenUtc = DateTime.UtcNow;
            _presence[connectionId] = state;
            return Task.CompletedTask;
        }

        public Task<PresenceState?> GetPresenceAsync(string connectionId)
        {
            if (_presence.TryGetValue(connectionId, out var s)) return Task.FromResult<PresenceState?>(s);
            return Task.FromResult<PresenceState?>(null);
        }
    }
}
