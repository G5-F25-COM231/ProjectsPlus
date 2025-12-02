// src/ProjectsPlus.Comms/Persistence/MessageRepository.InMemory.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    // In-memory IMessageRepository for local development and tests.
    // This file is intentionally self-contained so you can drop it into the Comms project.
    public interface IMessageRepository
    {
        Task SaveMessageAsync(ChatMessageDto msg, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetRoomMessagesAsync(Guid roomId, int limit, string? continuationToken, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetUserInboxAsync(Guid userId, int limit, string? continuationToken, CancellationToken ct = default);
        Task MarkMessageReadAsync(Guid userId, Guid messageId, CancellationToken ct = default);
        Task AddAttachmentAsync(AttachmentDescriptor att, CancellationToken ct = default);
        Task<IReadOnlyList<AttachmentDescriptor>> GetAttachmentsForMessageAsync(Guid messageId, CancellationToken ct = default);
    }

    public class MessageRepositoryInMemory : IMessageRepository
    {
        // message store keyed by MessageId
        private readonly ConcurrentDictionary<Guid, ChatMessageDto> _messages = new();
        // attachments keyed by AttachmentId
        private readonly ConcurrentDictionary<Guid, AttachmentDescriptor> _attachments = new();
        // simple audit of reads (messageId -> set of userIds)
        private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _reads = new();

        // lightweight lock for multi-step operations
        private readonly object _lock = new();

        public Task SaveMessageAsync(ChatMessageDto msg, CancellationToken ct = default)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            if (msg.MessageId == Guid.Empty) msg.MessageId = Guid.NewGuid();
            if (msg.CreatedAt == default) msg.CreatedAt = DateTime.UtcNow;
            msg.Snippet ??= msg.Body?.Length > 4000 ? msg.Body.Substring(0, 4000) : msg.Body;

            // ensure attachments reference the message id
            if (msg.Attachments != null)
            {
                foreach (var a in msg.Attachments)
                {
                    if (a.AttachmentId == Guid.Empty) a.AttachmentId = Guid.NewGuid();
                    a.MessageId = msg.MessageId;
                    if (a.CreatedAt == default) a.CreatedAt = DateTime.UtcNow;
                    _attachments[a.AttachmentId] = a;
                }
            }

            _messages[msg.MessageId] = CloneMessage(msg);

            return Task.CompletedTask;
        }

        public Task<PagedResult<ChatMessageDto>> GetRoomMessagesAsync(Guid roomId, int limit, string? continuationToken, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 50;

            DateTime? before = null;
            if (!string.IsNullOrWhiteSpace(continuationToken) && DateTime.TryParse(continuationToken, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                before = parsed;
            }

            var q = _messages.Values
                .Where(m => m.RoomId == roomId && !m.IsDeleted);

            if (before.HasValue) q = q.Where(m => m.CreatedAt < before.Value);

            var ordered = q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId).Take(limit).ToList();

            var dtos = ordered.Select(CloneMessage).ToList();

            string? next = null;
            if (ordered.Count == limit)
            {
                var last = ordered.Last();
                next = last.CreatedAt.ToString("o");
            }

            return Task.FromResult(new PagedResult<ChatMessageDto>
            {
                Items = dtos,
                ContinuationToken = next,
                TotalCount = dtos.Count
            });
        }

        public Task<PagedResult<ChatMessageDto>> GetUserInboxAsync(Guid userId, int limit, string? continuationToken, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 50;

            DateTime? before = null;
            if (!string.IsNullOrWhiteSpace(continuationToken) && DateTime.TryParse(continuationToken, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                before = parsed;
            }

            var q = _messages.Values
                .Where(m => m.RecipientUserId.HasValue && m.RecipientUserId.Value == userId && !m.IsDeleted);

            if (before.HasValue) q = q.Where(m => m.CreatedAt < before.Value);

            var ordered = q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.MessageId).Take(limit).ToList();

            var dtos = ordered.Select(CloneMessage).ToList();

            string? next = null;
            if (ordered.Count == limit)
            {
                var last = ordered.Last();
                next = last.CreatedAt.ToString("o");
            }

            return Task.FromResult(new PagedResult<ChatMessageDto>
            {
                Items = dtos,
                ContinuationToken = next,
                TotalCount = dtos.Count
            });
        }

        public Task MarkMessageReadAsync(Guid userId, Guid messageId, CancellationToken ct = default)
        {
            if (messageId == Guid.Empty || userId == Guid.Empty) return Task.CompletedTask;

            var set = _reads.GetOrAdd(messageId, _ => new ConcurrentDictionary<Guid, byte>());
            set[userId] = 1;

            // also write a lightweight audit entry into CommAudit store if you have one; omitted here for brevity.

            return Task.CompletedTask;
        }

        public Task AddAttachmentAsync(AttachmentDescriptor att, CancellationToken ct = default)
        {
            if (att == null) throw new ArgumentNullException(nameof(att));
            if (att.AttachmentId == Guid.Empty) att.AttachmentId = Guid.NewGuid();
            if (att.CreatedAt == default) att.CreatedAt = DateTime.UtcNow;

            _attachments[att.AttachmentId] = CloneAttachment(att);

            // if message exists, attach reference
            if (att.MessageId.HasValue && _messages.TryGetValue(att.MessageId.Value, out var msg))
            {
                lock (_lock)
                {
                    msg.Attachments ??= new List<AttachmentDescriptor>();
                    if (!msg.Attachments.Any(a => a.AttachmentId == att.AttachmentId))
                    {
                        msg.Attachments.Add(CloneAttachment(att));
                        _messages[msg.MessageId] = CloneMessage(msg);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AttachmentDescriptor>> GetAttachmentsForMessageAsync(Guid messageId, CancellationToken ct = default)
        {
            var items = _attachments.Values
                .Where(a => a.MessageId.HasValue && a.MessageId.Value == messageId)
                .OrderBy(a => a.CreatedAt)
                .Select(CloneAttachment)
                .ToList();

            return Task.FromResult((IReadOnlyList<AttachmentDescriptor>)items);
        }

        #region Helpers / cloning to avoid shared mutable state

        private static ChatMessageDto CloneMessage(ChatMessageDto src)
        {
            if (src == null) return null!;

            return new ChatMessageDto
            {
                MessageId = src.MessageId,
                RoomId = src.RoomId,
                ThreadRootId = src.ThreadRootId,
                SenderUserId = src.SenderUserId,
                RecipientUserId = src.RecipientUserId,
                Body = src.Body,
                BodyHtml = src.BodyHtml,
                Snippet = src.Snippet,
                CreatedAt = src.CreatedAt,
                EditedAt = src.EditedAt,
                IsDeleted = src.IsDeleted,
                Metadata = src.Metadata == null ? null : new Dictionary<string, object?>(src.Metadata),
                Attachments = src.Attachments == null ? null : src.Attachments.Select(CloneAttachment).ToList()
            };
        }

        private static AttachmentDescriptor CloneAttachment(AttachmentDescriptor src)
        {
            if (src == null) return null!;

            return new AttachmentDescriptor
            {
                AttachmentId = src.AttachmentId,
                MessageId = src.MessageId,
                Filename = src.Filename,
                ContentType = src.ContentType,
                StoragePointer = src.StoragePointer,
                DdbId = src.DdbId,
                SizeBytes = src.SizeBytes,
                UploaderUserId = src.UploaderUserId,
                CreatedAt = src.CreatedAt
            };
        }

        #endregion
    }

    #region DTO placeholders

    public sealed class ChatMessageDto
    {
        public Guid MessageId { get; set; }
        public Guid? RoomId { get; set; }
        public Guid? ThreadRootId { get; set; }
        public Guid? SenderUserId { get; set; }
        public Guid? RecipientUserId { get; set; }
        public string? Body { get; set; }
        public string? BodyHtml { get; set; }
        public string? Snippet { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; }
        public IDictionary<string, object?>? Metadata { get; set; }
        public List<AttachmentDescriptor>? Attachments { get; set; }
    }

    public sealed class AttachmentDescriptor
    {
        public Guid AttachmentId { get; set; }
        public Guid? MessageId { get; set; }
        public string? Filename { get; set; }
        public string? ContentType { get; set; }
        public string? StoragePointer { get; set; }
        public string? DdbId { get; set; }
        public long? SizeBytes { get; set; }
        public Guid? UploaderUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

   

    #endregion
}
