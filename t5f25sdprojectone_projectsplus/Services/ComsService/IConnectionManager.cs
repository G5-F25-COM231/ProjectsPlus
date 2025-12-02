// src/ProjectsPlus.Comms/Realtime/IConnectionManager.cs
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public interface IConnectionManager
    {
        Task RegisterAsync(string connectionId, Guid? userId, WebSocket socket);
        Task UnregisterAsync(string connectionId);
        bool TryGetSocket(string connectionId, out WebSocket? socket);
        IReadOnlyList<string> GetConnectionsForUser(Guid userId);
        Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task SendToUserAsync(Guid userId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task SetPresenceAsync(string connectionId, PresenceState state);
        Task<PresenceState?> GetPresenceAsync(string connectionId);
        event Func<string, Guid?, PresenceState, Task>? OnConnected;
        event Func<string, Guid?, Task>? OnDisconnected;
    }
}
