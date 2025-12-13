// src/ProjectsPlus.Comms/Realtime/IConnectionManager.cs
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface IConnectionManager
    {
        Task RegisterAsync(string connectionId, long? userId, WebSocket socket);
        Task UnregisterAsync(string connectionId, CancellationToken ct = default);
        bool TryGetSocket(string connectionId, out WebSocket? socket, CancellationToken ct = default);
        IReadOnlyList<string> GetConnectionsForUser(long userId, CancellationToken ct = default);
        Task SendToConnectionAsync(string connectionId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task SendToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task SetPresenceAsync(string connectionId, PresenceState state, CancellationToken ct = default);
        Task<PresenceState?> GetPresenceAsync(string connectionId, CancellationToken ct = default);
        event Func<string, long?, PresenceState, Task>? OnConnected;
        event Func<string, long?, Task>? OnDisconnected;

        Task BroadcastToUserAsync(long userId, RealtimeEnvelope envelope, CancellationToken ct = default);
        Task BroadcastToRoomAsync(Guid roomId, RealtimeEnvelope envelope, CancellationToken ct = default);
        
    }
}
