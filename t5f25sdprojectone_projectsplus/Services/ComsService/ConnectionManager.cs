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
        public long? UserId { get; set; }
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
        private readonly ConcurrentDictionary<long, ConcurrentBag<string>> _userIndex = new();
        private readonly ConcurrentDictionary<string, PresenceState> _presence = new();

        public event Func<string, long?, PresenceState, Task>? OnConnected;
        public event Func<string, long?, Task>? OnDisconnected;

        // Interface requires RegisterAsync(string, Guid?, WebSocket)
        public Task RegisterAsync(string connectionId, long? userId, WebSocket socket)
            => RegisterAsync(connectionId, userId, socket, CancellationToken.None);

        // Internal implementation with CancellationToken (keeps existing behavior)
        public async Task RegisterAsync(string connectionId, long? userId, WebSocket socket, CancellationToken ct = default)
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

            if (OnConnected != null)
            {
                try
                {
                    await OnConnected.Invoke(connectionId, userId, presence).ConfigureAwait(false);
                }
                catch
                {
                    // swallow handler exceptions to avoid breaking registration flow
                }
            }
        }

        // Interface requires UnregisterAsync(string, CancellationToken)
        public async Task UnregisterAsync(string connectionId, CancellationToken ct = default)
        {
            if (_connections.TryRemove(connectionId, out var info))
            {
                if (info.UserId.HasValue && _userIndex.TryGetValue(info.UserId.Value, out var bag))
                {
                    // Rebuild bag without the removed connectionId
                    var remaining = new ConcurrentBag<string>(bag.Where(c => c != connectionId));
                    _userIndex[info.UserId.Value] = remaining;
                }

                _presence.TryRemove(connectionId, out _);

                if (OnDisconnected != null)
                {
                    try
                    {
                        await OnDisconnected.Invoke(connectionId, info.UserId).ConfigureAwait(false);
                    }
                    catch
                    {
                        // swallow
                    }
                }
            }
        }

        // Interface requires TryGetSocket(string, out WebSocket?, CancellationToken)
        public bool TryGetSocket(string connectionId, out WebSocket? socket, CancellationToken ct = default)
        {
            socket = null;
            if (_connections.TryGetValue(connectionId, out var info))
            {
                socket = info.Socket;
                return true;
            }
            return false;
        }

        // Interface requires GetConnectionsForUser(Guid, CancellationToken)
        public IReadOnlyList<string> GetConnectionsForUser(long userId, CancellationToken ct = default)
        {
            if (_userIndex.TryGetValue(userId, out var bag))
                return bag.ToArray();
            return Array.Empty<string>();
        }

        // Existing SendToConnectionAsync signature matches interface
        public async Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            if (!TryGetSocket(connectionId, out var socket, ct) || socket == null || socket.State != WebSocketState.Open) return;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
        }

        // Interface requires SendToUserAsync(Guid, RealtimeEnvelope, CancellationToken)
        public async Task SendToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            await BroadcastToUserAsync(userId, envelope, ct).ConfigureAwait(false);
        }

        // Interface requires BroadcastToUserAsync(Guid, RealtimeEnvelope, CancellationToken)
        public async Task BroadcastToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            var conns = GetConnectionsForUser(userId, ct);
            var tasks = conns.Select(c => SendToConnectionAsync(c, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Interface requires BroadcastToRoomAsync(string, RealtimeEnvelope, CancellationToken)
        // Keep existing Guid-based overload for convenience; implement string-based to satisfy interface.
        public async Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // Try parse roomId as Guid and delegate to Guid overload if successful
            if (Guid.TryParse(roomId, out var rg))
            {
                await BroadcastToRoomAsync(rg, envelope, ct).ConfigureAwait(false);
                return;
            }

            // Fallback: broadcast to all connections (caller should resolve membership when possible)
            var tasks = _connections.Keys.Select(c => SendToConnectionAsync(c, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Existing Guid-based BroadcastToRoomAsync (keeps compatibility with earlier code)
        public async Task BroadcastToRoomAsync(Guid roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            // Room membership resolution is external (ChatroomService). As a fallback, broadcast to all connections.
            // Prefer callers to resolve members and call SendToUserAsync for targeted delivery.
            var tasks = _connections.Keys.Select(c => SendToConnectionAsync(c, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        // Interface requires SetPresenceAsync(string, PresenceState, CancellationToken)
        public Task SetPresenceAsync(string connectionId, PresenceState presenceState, CancellationToken ct = default)
        {
            presenceState.LastSeenUtc = DateTime.UtcNow;
            _presence[connectionId] = presenceState;
            return Task.CompletedTask;
        }

        // Interface requires GetPresenceAsync(string, CancellationToken)
        public Task<PresenceState?> GetPresenceAsync(string connectionId, CancellationToken ct = default)
        {
            if (_presence.TryGetValue(connectionId, out var s)) return Task.FromResult<PresenceState?>(s);
            return Task.FromResult<PresenceState?>(null);
        }
    }
}
