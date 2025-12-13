// src/ProjectsPlus.Comms/Realtime/MessageCenter.cs
using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// MessageCenter: orchestrates inbound realtime envelopes.
    /// - Persists chat messages via IMessageRepository
    /// - Routes/broadcasts via IChatroomService or IConnectionManager
    /// - Updates presence via IPresenceService
    /// - Records simple acks/receipts via ICommAuditStore
    /// </summary>
    public class MessageCenter : IMessageCenter
    {
        private readonly IMessageRepository _messageRepo;
        private readonly IChatroomService _chatroom;
        private readonly ICommAuditStore _auditStore;
        private readonly IPresenceService _presence;
        private readonly IConnectionManager? _connectionManager;
        private readonly ILogger<MessageCenter> _logger;

        public MessageCenter(
            IMessageRepository messageRepo,
            IChatroomService chatroom,
            ICommAuditStore auditStore,
            IPresenceService presence,
            ILogger<MessageCenter> logger,
            IConnectionManager? connectionManager = null)
        {
            _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
            _chatroom = chatroom ?? throw new ArgumentNullException(nameof(chatroom));
            _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
            _presence = presence ?? throw new ArgumentNullException(nameof(presence));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionManager = connectionManager;
        }

        public async Task HandleRealtimeInboundAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct = default)
        {
            if (envelope == null)
            {
                _logger.LogWarning("Null envelope received on connection {ConnectionId}", connectionId);
                return;
            }

            var type = (envelope.Type ?? "message").Trim().ToLowerInvariant();

            try
            {
                switch (type)
                {
                    case "message":
                    case "chat.message":
                    case "chat.post":
                        await HandleChatMessageAsync(envelope, connectionId, userId, ct).ConfigureAwait(false);
                        break;

                    case "presence":
                    case "presence.update":
                        await HandlePresenceAsync(envelope, connectionId, userId, ct).ConfigureAwait(false);
                        break;

                    case "typing":
                        await HandleTypingAsync(envelope, connectionId, userId, ct).ConfigureAwait(false);
                        break;

                    case "ack":
                    case "delivery":
                    case "receipt":
                        await HandleAckAsync(envelope, connectionId, userId, ct).ConfigureAwait(false);
                        break;

                    default:
                        _logger.LogInformation("Unhandled envelope type {Type} from connection {Connection}", envelope.Type, connectionId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling envelope type {Type} from connection {Connection}", envelope.Type, connectionId);
            }
        }

        private async Task HandleChatMessageAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct)
        {
            // Determine roomId or toUserId from envelope.To or payload keys
            Guid? roomId = TryParseGuid(envelope.To);
            long? toUserId = null;
            if (envelope.Payload != null && envelope.Payload.TryGetValue("toUserId", out var toVal) && toVal != null)
            {
                if (long.TryParse(toVal.ToString(), out var g)) toUserId = g;
            }

            // If neither room nor direct recipient, try payload for roomId
            if (!roomId.HasValue && envelope.Payload != null && envelope.Payload.TryGetValue("roomId", out var rVal) && rVal != null)
            {
                if (Guid.TryParse(rVal.ToString(), out var rg)) roomId = rg;
            }

            if (!roomId.HasValue && !toUserId.HasValue)
            {
                _logger.LogWarning("Chat message missing destination (room or toUser) from connection {Connection}", connectionId);
                return;
            }

            var msg = new ChatMessageDto
            {
                MessageId = Guid.TryParse(envelope.Id, out var mid) ? mid : Guid.NewGuid(),
                RoomId = roomId,
                SenderUserId = userId,
                RecipientUserId = toUserId,
                Body = envelope.Payload != null && envelope.Payload.TryGetValue("body", out var b) ? b?.ToString() : envelope.Body,
                CreatedAt = envelope.Timestamp,
                Metadata = envelope.Meta != null ? ConvertMeta(envelope.Meta) : null
            };

            // Persist message (best-effort)
            try
            {
                await _messageRepo.SaveMessageAsync(msg, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist message {MessageId}", msg.MessageId);
            }

            // Broadcast
            if (roomId.HasValue)
            {
                try
                {
                    await _chatroom.HandleRealtimeMessageAsync(new RealtimeEnvelope
                    {
                        Id = envelope.Id,
                        Type = envelope.Type,
                        From = envelope.From,
                        To = envelope.To,
                        Payload = envelope.Payload,
                        Meta = envelope.Meta,
                        Timestamp = envelope.Timestamp
                    }, connectionId, userId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Chatroom service failed for room {RoomId}", roomId);
                    if (_connectionManager != null)
                    {
                        var ws = new RealtimeEnvelope { Type = "message", MessageId = msg.MessageId.ToString(), RoomId = roomId.ToString(), Body = msg.Body };
                        await _connectionManager.BroadcastToRoomAsync(roomId.Value, ws, ct).ConfigureAwait(false);
                    }
                }
            }
            else if (toUserId.HasValue)
            {
                if (_connectionManager != null)
                {
                    var ws = new RealtimeEnvelope { Type = "message", MessageId = msg.MessageId.ToString(), To = toUserId.ToString(), Body = msg.Body };
                    await _connectionManager.BroadcastToUserAsync(toUserId.Value, ws).ConfigureAwait(false);
                }
            }
        }

        private async Task HandlePresenceAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct)
        {
            if (userId == null)
            {
                _logger.LogWarning("Presence update from anonymous connection {ConnectionId} ignored", connectionId);
                return;
            }

            // Expect payload or meta to contain "status" and optional metadata
            string? status = null;
            IDictionary<string, object?>? metadata = null;

            if (envelope.Payload != null && envelope.Payload.TryGetValue("status", out var s)) status = s?.ToString();
            if (envelope.Meta != null && envelope.Meta.TryGetValue("metadata", out var m) && m is IDictionary<string, object?> md) metadata = md;
            if (metadata == null && envelope.Payload != null && envelope.Payload.TryGetValue("metadata", out var pm) && pm is IDictionary<string, object?> pmd) metadata = pmd;

            var metadataJson = metadata != null ? System.Text.Json.JsonSerializer.Serialize(metadata) : null;

            if (string.IsNullOrWhiteSpace(status) || status!.Equals("offline", StringComparison.OrdinalIgnoreCase))
            {
                await _presence.RemovePresenceAsync(userId.Value, connectionId, ct).ConfigureAwait(false);
            }
            else
            {
                await _presence.SetPresenceAsync(userId.Value, connectionId, status!, metadataJson, ct).ConfigureAwait(false);
            }

            // Optionally record a lightweight audit entry
            try
            {
                await _auditStore.RecordAsync(new CommunicationAuditEntryDto
                {
                    AuditId = Guid.NewGuid(),
                    NotificationId = Guid.Empty,
                    Channel = ChannelType.InApp,
                    Recipient = userId.ToString(),
                    Status = SendStatus.Sent,
                    Timestamp = DateTime.UtcNow,
                    Metadata = metadata != null ? ConvertMetaToStringMap(metadata) : null,
                    PayloadJson = null
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to record presence audit for {User}", userId);
            }
        }

        private async Task HandleTypingAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct)
        {
            // Broadcast typing indicator to room or user
            Guid? roomId = TryParseGuid(envelope.To);
            long? toUserId = null;
            if (envelope.Payload != null && envelope.Payload.TryGetValue("toUserId", out var t) && t != null && long.TryParse(t.ToString(), out var tg)) toUserId = tg;

            var body = envelope.Payload != null && envelope.Payload.TryGetValue("text", out var txt) ? txt?.ToString() : null;

            if (roomId.HasValue)
            {
                if (_connectionManager != null)
                {
                    var ws = new RealtimeEnvelope { Type = "typing", RoomId = roomId.ToString(), Body = body };
                    await _connectionManager.BroadcastToRoomAsync(roomId.Value, ws).ConfigureAwait(false);
                }
            }
            else if (toUserId.HasValue)
            {
                if (_connectionManager != null)
                {
                    var ws = new RealtimeEnvelope { Type = "typing", To = toUserId.ToString(), Body = body };
                    await _connectionManager.BroadcastToUserAsync(toUserId.Value, ws, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task HandleAckAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct)
        {
            // Acks/receipts: payload should contain messageId and status
            if (envelope.Payload == null) return;
            if (!envelope.Payload.TryGetValue("messageId", out var midObj) || midObj == null) return;

            var messageIdStr = midObj.ToString();
            var status = envelope.Payload.TryGetValue("status", out var s) ? s?.ToString() : "ack";

            try
            {
                await _auditStore.RecordAsync(new CommunicationAuditEntryDto
                {
                    AuditId = Guid.NewGuid(),
                    NotificationId = Guid.Empty,
                    Channel = ChannelType.InApp,
                    Recipient = userId?.ToString() ?? connectionId,
                    Status = SendStatus.Delivered,
                    Timestamp = DateTime.UtcNow,
                    Metadata = envelope.Meta != null ? ConvertMetaToStringMap(envelope.Meta) : null,
                    PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { messageId = messageIdStr, status })
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to record ack for message {MessageId}", messageIdStr);
            }
        }

        #region Helpers

        private static Guid? TryParseGuid(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return Guid.TryParse(s, out var g) ? g : null;
        }

        private static IDictionary<string, object?>? ConvertMeta(IDictionary<string, object?>? meta)
        {
            return meta == null ? null : new Dictionary<string, object?>(meta);
        }

        private static IDictionary<string, string> ConvertMetaToStringMap(IDictionary<string, object?> meta)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in meta)
            {
                dict[kv.Key] = kv.Value?.ToString() ?? string.Empty;
            }
            return dict;
        }

        private static long ConvertUserIdToLong(Guid userId)
        {
            // Deterministic, non-cryptographic mapping from Guid to positive long.
            // If your system already uses numeric user ids, replace this with the real mapping.
            var bytes = userId.ToByteArray();
            // Use first 8 bytes as little-endian long
            long val = BitConverter.ToInt64(bytes, 0);
            return Math.Abs(val);
        }

        #endregion
    }

    /// <summary>
    /// Minimal IConnectionManager used by MessageCenter for broadcasting.
    /// Replace with your project's concrete connection manager if available.
    /// </summary>
   
}
