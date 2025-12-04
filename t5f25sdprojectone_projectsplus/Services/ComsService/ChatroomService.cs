// src/ProjectsPlus.Comms/Chatroom/EfChatroomService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class ChatroomService : IChatroomService
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly IConnectionManager _connections;
        private readonly ILogger<ChatroomService> _logger;

        public ChatroomService(ProjectsPlusDbContext db, IConnectionManager connections, ILogger<ChatroomService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<RoomDto> CreateRoomAsync(Guid workspaceId, string name, long createdBy, bool isPrivate = true, CancellationToken ct = default)
        {
            var room = new RoomEntity
            {
                RoomId = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                Name = name,
                IsPrivate = isPrivate,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow
            };

            _db.Rooms.Add(room);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            return new RoomDto
            {
                RoomId = room.RoomId,
                WorkspaceId = room.WorkspaceId,
                Name = room.Name,
                IsPrivate = room.IsPrivate,
                CreatedBy = room.CreatedBy,
                CreatedAt = room.CreatedAt
            };
        }

        public async Task JoinRoomAsync(Guid roomId, long userId, CancellationToken ct = default)
        {
            var exists = await _db.RoomMembers.AnyAsync(m => m.RoomId == roomId && m.UserId == userId, ct).ConfigureAwait(false);
            if (exists) return;

            var member = new RoomMemberEntity
            {
                RoomId = roomId,
                UserId = userId,
                Role = "member",
                JoinedAt = DateTime.UtcNow
            };

            _db.RoomMembers.Add(member);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var env = new RealtimeEnvelope
            {
                Type = "chat.member.joined",
                To = roomId.ToString("D"),
                Payload = new Dictionary<string, object?> { ["userId"] = userId, ["roomId"] = roomId }
            };

            try { await _connections.BroadcastToRoomAsync(roomId, env).ConfigureAwait(false); } catch (Exception ex) { _logger.LogDebug(ex, "Broadcast join failed"); }
        }

        public async Task LeaveRoomAsync(Guid roomId, long userId, CancellationToken ct = default)
        {
            var member = await _db.RoomMembers.FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == userId, ct).ConfigureAwait(false);
            if (member == null) return;
            _db.RoomMembers.Remove(member);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var env = new RealtimeEnvelope
            {
                Type = "chat.member.left",
                To = roomId.ToString("D"),
                Payload = new Dictionary<string, object?> { ["userId"] = userId, ["roomId"] = roomId }
            };

            try { await _connections.BroadcastToRoomAsync(roomId.ToString("D"), env).ConfigureAwait(false); } catch (Exception ex) { _logger.LogDebug(ex, "Broadcast leave failed"); }
        }

        public async Task<ChatMessageDto> PostMessageAsync(Guid roomId, long senderUserId, string body, IEnumerable<AttachmentDescriptor>? attachments = null, CancellationToken ct = default)
        {
            var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId, ct).ConfigureAwait(false);
            if (room == null) throw new InvalidOperationException("Room not found");

            var msg = new MessageEntity
            {
                MessageId = Guid.NewGuid(),
                RoomId = roomId,
                ThreadRootId = null,
                SenderUserId = senderUserId,
                RecipientUserId = null,
                Body = body,
                BodyHtml = null,
                Snippet = body?.Length > 4000 ? body.Substring(0, 4000) : body,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false,
                Visibility = "visible",
                MetadataJson = attachments == null ? null : JsonSerializer.Serialize(attachments)
            };

            _db.Messages.Add(msg);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var dto = new ChatMessageDto
            {
                MessageId = msg.MessageId,
                RoomId = msg.RoomId,
                SenderUserId = msg.SenderUserId,
                Body = msg.Body,
                Attachments = attachments?.ToList(),
                CreatedAt = msg.CreatedAt
            };

            var envelope = new RealtimeEnvelope
            {
                Type = "chat.message",
                To = roomId.ToString("D"),
                Payload = new Dictionary<string, object?>
                {
                    ["messageId"] = dto.MessageId,
                    ["roomId"] = dto.RoomId,
                    ["senderUserId"] = dto.SenderUserId,
                    ["body"] = dto.Body,
                    ["createdAt"] = dto.CreatedAt
                }
            };

            try { await _connections.BroadcastToRoomAsync(roomId, envelope).ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "BroadcastToRoomAsync failed for room {RoomId}", roomId); }

            return dto;
        }

        public async Task<IReadOnlyList<ChatMessageDto>> GetRoomMembersAsync(Guid roomId, CancellationToken ct = default)
        {
            // Interface returns ChatMessageDto; map members to lightweight ChatMessageDto placeholders
            var members = await _db.RoomMembers
                .Where(m => m.RoomId == roomId)
                .OrderBy(m => m.JoinedAt)
                .Select(m => new ChatMessageDto
                {
                    MessageId = Guid.Empty,
                    RoomId = m.RoomId,
                    SenderUserId = m.UserId,
                    Body = string.Empty,
                    Attachments = null,
                    CreatedAt = m.JoinedAt
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return members;
        }

        public async Task KickMemberAsync(Guid roomId, long moderatorUserId, long targetUserId, CancellationToken ct = default)
        {
            var mod = await _db.RoomMembers.FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == moderatorUserId, ct).ConfigureAwait(false);
            if (mod == null || !string.Equals(mod.Role, "moderator", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Moderator privileges required");

            var target = await _db.RoomMembers.FirstOrDefaultAsync(m => m.RoomId == roomId && m.UserId == targetUserId, ct).ConfigureAwait(false);
            if (target == null) return;

            _db.RoomMembers.Remove(target);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            var envelope = new RealtimeEnvelope
            {
                Type = "chat.kick",
                To = targetUserId.ToString("D"),
                Payload = new Dictionary<string, object?> { ["roomId"] = roomId, ["kickedBy"] = moderatorUserId }
            };

            try { await _connections.SendToUserAsync(targetUserId, envelope, ct).ConfigureAwait(false); } catch { }
            try { await _connections.BroadcastToRoomAsync(roomId, envelope, ct).ConfigureAwait(false); } catch { }
        }

        public Task AddConnectionToRoomAsync(string roomId, string connectionId, long? userId)
        {
            var env = new RealtimeEnvelope
            {
                Type = "presence.join",
                To = roomId,
                Payload = new Dictionary<string, object?> { ["connectionId"] = connectionId, ["userId"] = userId?.ToString("D") }
            };
            return _connections.BroadcastToRoomAsync(Guid.Parse(roomId), env);
        }

        public Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, long? userId)
        {
            var env = new RealtimeEnvelope
            {
                Type = "presence.leave",
                To = roomId,
                Payload = new Dictionary<string, object?> { ["connectionId"] = connectionId, ["userId"] = userId?.ToString("D") }
            };
            return _connections.BroadcastToRoomAsync(Guid.Parse(roomId), env);
        }

        public async Task HandleRealtimeMessageAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct = default)
        {
            if (envelope?.Payload == null) return;

            if (!envelope.Payload.TryGetValue("roomId", out var roomObj) || roomObj == null) return;
            if (!Guid.TryParse(roomObj.ToString(), out var roomId)) return;

            var body = envelope.Payload.TryGetValue("body", out var b) ? b?.ToString() ?? string.Empty : string.Empty;

            List<AttachmentDescriptor>? attachments = null;
            if (envelope.Payload.TryGetValue("attachments", out var a) && a is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                try { attachments = JsonSerializer.Deserialize<List<AttachmentDescriptor>>(je.GetRawText()); } catch { attachments = null; }
            }

            var senderId = userId ?? null;
            await PostMessageAsync(roomId, (long)senderId, body, attachments, ct).ConfigureAwait(false);
        }
    }
}
