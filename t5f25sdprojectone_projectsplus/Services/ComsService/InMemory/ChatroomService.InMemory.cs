// src/ProjectsPlus.Comms/Chatroom/InMemoryChatroomService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    public class InMemoryChatroomService : IChatroomService
    {
        private readonly IConnectionManager _connections;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long?>> _rooms = new();

        public InMemoryChatroomService(IConnectionManager connections)
        {
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        }

        public Task CreateRoomAsync(string roomId, string? displayName = null, CancellationToken ct = default)
        {
            _rooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, long?>());
            return Task.CompletedTask;
        }

        public Task DeleteRoomAsync(string roomId, CancellationToken ct = default)
        {
            _rooms.TryRemove(roomId, out _);
            return Task.CompletedTask;
        }

        public Task AddConnectionToRoomAsync(string roomId, string connectionId, long? userId, CancellationToken ct = default)
        {
            var members = _rooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<string, long?>());
            members[connectionId] = userId;
            return Task.CompletedTask;
        }

        public Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, long? userId, CancellationToken ct = default)
        {
            if (_rooms.TryGetValue(roomId, out var members))
            {
                members.TryRemove(connectionId, out _);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListMembersAsync(string roomId, CancellationToken ct = default)
        {
            if (_rooms.TryGetValue(roomId, out var members))
                return Task.FromResult((IReadOnlyList<string>)members.Keys.ToList());
            return Task.FromResult((IReadOnlyList<string>)Array.Empty<string>());
        }

        public async Task BroadcastToRoomAsync(string roomId, RealtimeEnvelope envelope, CancellationToken ct = default)
        {
            if (!_rooms.TryGetValue(roomId, out var members)) return;
            var tasks = members.Keys.Select(conn => _connections.SendToConnectionAsync(conn, envelope, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public Task<bool> IsMemberAsync(string roomId, string connectionId, CancellationToken ct = default)
        {
            if (_rooms.TryGetValue(roomId, out var members))
                return Task.FromResult(members.ContainsKey(connectionId));
            return Task.FromResult(false);
        }

        public Task<RoomDto> CreateRoomAsync(Guid workspaceId, string name, Guid createdBy, bool isPrivate = true, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task JoinRoomAsync(Guid roomId, Guid userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task LeaveRoomAsync(Guid roomId, Guid userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<ChatMessageDto> PostMessageAsync(Guid roomId, Guid senderUserId, string body, IEnumerable<AttachmentDescriptor>? attachments = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<ChatMessageDto>> GetRoomMembersAsync(Guid roomId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task KickMemberAsync(Guid roomId, Guid moderatorUserId, Guid targetUserId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task AddConnectionToRoomAsync(string roomId, string connectionId, Guid? userId)
        {
            throw new NotImplementedException();
        }

        public Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, Guid? userId)
        {
            throw new NotImplementedException();
        }

        public Task HandleRealtimeMessageAsync(RealtimeEnvelope envelope, string connectionId, Guid? userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<ComsService.ChatMessageDto> PostMessageAsync(Guid roomId, Guid senderUserId, string body, IEnumerable<ComsService.AttachmentDescriptor>? attachments = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        Task<IReadOnlyList<ComsService.ChatMessageDto>> IChatroomService.GetRoomMembersAsync(Guid roomId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<RoomDto> CreateRoomAsync(Guid workspaceId, string name, long createdBy, bool isPrivate = true, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task JoinRoomAsync(Guid roomId, long userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task LeaveRoomAsync(Guid roomId, long userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<ComsService.ChatMessageDto> PostMessageAsync(Guid roomId, long senderUserId, string body, IEnumerable<ComsService.AttachmentDescriptor>? attachments = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task KickMemberAsync(Guid roomId, long moderatorUserId, long targetUserId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task AddConnectionToRoomAsync(string roomId, string connectionId, long? userId)
        {
            throw new NotImplementedException();
        }

        public Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, long? userId)
        {
            throw new NotImplementedException();
        }

        public Task HandleRealtimeMessageAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }
}
