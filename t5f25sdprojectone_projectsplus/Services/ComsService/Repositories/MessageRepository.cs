// src/ProjectsPlus.Comms/Persistence/MessageRepository.Ef.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public class MessageRepository : IMessageRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public MessageRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task SaveMessageAsync(ChatMessageDto msg, CancellationToken ct = default)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            var entity = new MessageEntity
            {
                MessageId = msg.MessageId == Guid.Empty ? Guid.NewGuid() : msg.MessageId,
                RoomId = msg.RoomId,
                ThreadRootId = msg.ThreadRootId,
                SenderUserId = msg.SenderUserId,
                RecipientUserId = msg.RecipientUserId,
                Body = msg.Body,
                BodyHtml = msg.BodyHtml,
                Snippet = msg.Snippet ?? (msg.Body?.Length > 4000 ? msg.Body.Substring(0, 4000) : msg.Body),
                CreatedAt = msg.CreatedAt == default ? DateTime.UtcNow : msg.CreatedAt,
                EditedAt = msg.EditedAt,
                IsDeleted = msg.IsDeleted,
                Visibility = msg.Metadata != null && msg.Metadata.TryGetValue("visibility", out var v) ? v?.ToString() : "visible",
                MetadataJson = msg.Metadata == null ? null : JsonSerializer.Serialize(msg.Metadata)
            };

            using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.Messages!.AddAsync(entity, ct);
                if (msg.Attachments != null && msg.Attachments.Count > 0)
                {
                    foreach (var a in msg.Attachments)
                    {
                        var att = new MessageAttachmentEntity
                        {
                            AttachmentId = a.AttachmentId == Guid.Empty ? Guid.NewGuid() : a.AttachmentId,
                            MessageId = entity.MessageId,
                            Filename = a.Filename,
                            ContentType = a.ContentType,
                            StoragePointer = a.StoragePointer,
                            DdbId = a.DdbId,
                            SizeBytes = a.SizeBytes,
                            UploaderUserId = a.UploaderUserId ?? null,
                            CreatedAt = a.CreatedAt
                        };
                        await _db.MessageAttachments!.AddAsync(att, ct);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<PagedResult<ChatMessageDto>> GetRoomMessagesAsync(Guid roomId, int limit, string? continuationToken, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 50;
            // continuationToken expected as ISO 8601 UTC datetime string of last seen CreatedAt (exclusive)
            DateTime? before = null;
            if (!string.IsNullOrWhiteSpace(continuationToken) && DateTime.TryParse(continuationToken, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                before = parsed;
            }

            var q = _db.Messages!.AsNoTracking().Where(m => m.RoomId == roomId && !m.IsDeleted);
            if (before.HasValue) q = q.Where(m => m.CreatedAt < before.Value);

            var items = await q.OrderByDescending(m => m.CreatedAt)
                               .ThenByDescending(m => m.MessageId)
                               .Take(limit)
                               .ToListAsync(ct);

            var dtos = items.Select(MapToDto).ToList();
            string? next = null;
            if (items.Count == limit)
            {
                var last = items.Last();
                next = last.CreatedAt.ToString("o");
            }

            return new PagedResult<ChatMessageDto>
            {
                Items = dtos,
                ContinuationToken = next,
                TotalCount = dtos.Count
            };
        }

        public async Task<PagedResult<ChatMessageDto>> GetUserInboxAsync(long userId, int limit, string? continuationToken, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 50;
            DateTime? before = null;
            if (!string.IsNullOrWhiteSpace(continuationToken) && DateTime.TryParse(continuationToken, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                before = parsed;
            }

            var q = _db.Messages!.AsNoTracking().Where(m => m.RecipientUserId == userId && !m.IsDeleted);
            // Note: your existing UserEntity uses long Ids; if you use GUIDs for users adjust accordingly.
            if (before.HasValue) q = q.Where(m => m.CreatedAt < before.Value);

            var items = await q.OrderByDescending(m => m.CreatedAt)
                               .ThenByDescending(m => m.MessageId)
                               .Take(limit)
                               .ToListAsync(ct);

            var dtos = items.Select(MapToDto).ToList();
            string? next = null;
            if (items.Count == limit)
            {
                var last = items.Last();
                next = last.CreatedAt.ToString("o");
            }

            return new PagedResult<ChatMessageDto>
            {
                Items = dtos,
                ContinuationToken = next,
                TotalCount = dtos.Count
            };
        }

        public async Task MarkMessageReadAsync(Guid userId, Guid messageId, CancellationToken ct = default)
        {
            // Simple implementation: persist a PresenceEvent-like audit or update a UserInbox table if you have one.
            // Here we write a CommAudit entry to record the read event.
            var audit = new CommAuditEntity
            {
                AuditId = Guid.NewGuid(),
                NotificationId = null,
                Channel = "InApp",
                Recipient = userId.ToString(),
                Status = "Read",
                ProviderMessageId = null,
                ErrorMessage = null,
                Timestamp = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new { messageId = messageId.ToString(), action = "mark_read" })
            };

            await _db.CommAudit!.AddAsync(audit, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task AddAttachmentAsync(AttachmentDescriptor att, CancellationToken ct = default)
        {
            if (att == null) throw new ArgumentNullException(nameof(att));

            var entity = new MessageAttachmentEntity
            {
                AttachmentId = att.AttachmentId == Guid.Empty ? Guid.NewGuid() : att.AttachmentId,
                MessageId = att.MessageId,
                Filename = att.Filename,
                ContentType = att.ContentType,
                StoragePointer = att.StoragePointer,
                DdbId = att.DdbId,
                SizeBytes = att.SizeBytes,
                UploaderUserId = att.UploaderUserId ?? null,
                CreatedAt = att.CreatedAt
            };

            await _db.MessageAttachments!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<AttachmentDescriptor>> GetAttachmentsForMessageAsync(Guid messageId, CancellationToken ct = default)
        {
            var items = await _db.MessageAttachments!.AsNoTracking()
                .Where(a => a.MessageId == messageId)
                .OrderBy(a => a.CreatedAt)
                .ToListAsync(ct);

            var dtos = items.Select(a => new AttachmentDescriptor
            {
                AttachmentId = a.AttachmentId,
                MessageId = a.MessageId,
                Filename = a.Filename ?? string.Empty,
                ContentType = a.ContentType,
                StoragePointer = a.StoragePointer ?? string.Empty,
                DdbId = a.DdbId,
                SizeBytes = a.SizeBytes,
                UploaderUserId = a.UploaderUserId.HasValue ? a.UploaderUserId.Value : null,
                CreatedAt = a.CreatedAt
            }).ToList();

            return dtos;
        }

        #region Helpers

        private static ChatMessageDto MapToDto(MessageEntity e)
        {
            var meta = string.IsNullOrWhiteSpace(e.MetadataJson)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, object?>>(e.MetadataJson);

            return new ChatMessageDto
            {
                MessageId = e.MessageId,
                RoomId = e.RoomId,
                ThreadRootId = e.ThreadRootId,
                SenderUserId = e.SenderUserId ?? null,
                RecipientUserId = e.RecipientUserId ?? null,
                Body = e.Body,
                BodyHtml = e.BodyHtml,
                Snippet = e.Snippet,
                CreatedAt = e.CreatedAt,
                EditedAt = e.EditedAt,
                IsDeleted = e.IsDeleted,
                Metadata = meta
            };
        }

        // NOTE: Your existing user model uses long Ids. These helper conversions are placeholders.
        // Replace with your actual mapping between long user ids and GUIDs if different.
        public static Guid ConvertLongToGuid(long id)
        {
            // Deterministic conversion for seeded/demo purposes: embed long into GUID bytes.
            var bytes = new byte[16];
            BitConverter.GetBytes(id).CopyTo(bytes, 0);
            return new Guid(bytes);
        }
        public static Guid? ConvertLongToGuidNullable(long? g) => g.HasValue ? ConvertLongToGuid(g.Value) : null;
        private static long ConvertGuidToLong(Guid guid)
        {
            var bytes = guid.ToByteArray();
            return BitConverter.ToInt64(bytes, 0);
        }

        public static long ConvertGuidToLong(Guid? guid)
        {
            if (!guid.HasValue) return 0;
            return ConvertGuidToLong(guid.Value);
        }

        public static Guid ConvertGuidToLong(Guid guid, bool unused) => guid; // placeholder to satisfy overloads if needed

        public static long ConvertGuidToLong(object? value)
        {
            // fallback
            return 0;
        }

        public static long ConvertGuidToLongNullable(Guid? g) => g.HasValue ? ConvertGuidToLong(g.Value) : 0;

        #endregion
    }
}
