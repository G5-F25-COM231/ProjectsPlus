// src/ProjectsPlus.Comms/Persistence/NotificationRepository.Ef.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using CommAuditEntity = t5f25sdprojectone_projectsplus.Models.Communication.CommAuditEntity;
using DeadLetterEntity = t5f25sdprojectone_projectsplus.Models.Communication.DeadLetterEntity;
using NotificationEntity = t5f25sdprojectone_projectsplus.Models.Communication.NotificationEntity;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    /// <summary>
    /// EF-backed implementation for notification persistence and durable queue operations.
    /// Implements both INotificationRepository and ICommQueue.
    /// </summary>
    public class NotificationRepository : INotificationRepository, ICommQueue
    {
        private readonly ProjectsPlusDbContext _db;

        public NotificationRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #region INotificationRepository

        public async Task EnqueueNotificationAsync(NotificationDto note, CancellationToken ct = default)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));

            var entity = new NotificationEntity
            {
                NotificationId = note.NotificationId == Guid.Empty ? Guid.NewGuid() : note.NotificationId,
                Channel = note.Channel.ToString(),
                Recipient = note.Recipient ?? string.Empty,
                Subject = note.Subject,
                Body = note.Body,
                VariablesJson = note.Variables == null ? null : JsonSerializer.Serialize(note.Variables),
                Priority = (byte)note.Priority,
                Status = SendStatus.Pending.ToString(),
                Attempts = 0,
                ScheduledFor = note.ScheduledFor,
                CreatedAt = note.CreatedAt == default ? DateTime.UtcNow : note.CreatedAt,
                MetadataJson = note.Metadata == null ? null : JsonSerializer.Serialize(note.Metadata),
            };

            await _db.Notifications!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<NotificationDto>> DequeuePendingAsync(int max, CancellationToken ct = default)
        {
            return (await DequeueInternalAsync(max, ct)).ToList();
        }

        //public async Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, CancellationToken ct = default)
        //{
        //    var entity = await _db.Notifications!.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);
        //    if (entity == null) return;

        //    entity.Attempts = attempts;
        //    entity.ProviderMessageId = providerMessageId;
        //    entity.Status = status ?? entity.Status;
        //    entity.LastAttemptAt = DateTime.UtcNow;
        //    //entity.ScheduledFor = DateTime.UtcNow;

        //    _db.Notifications.Update(entity);
        //    await _db.SaveChangesAsync(ct);
        //}

        // UpdateNotificationAttemptAsync snippet
        public async Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, DateTime? scheduledFor = null, CancellationToken ct = default)
        {
            var e = await _db.Notifications!.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);
            if (e == null) return;

            e.Attempts = attempts;
            e.ProviderMessageId = providerMessageId ?? e.ProviderMessageId;
            if (!string.IsNullOrWhiteSpace(status)) e.Status = status;
            e.ScheduledFor = scheduledFor ?? e.ScheduledFor;
            e.LastAttemptAt = DateTime.UtcNow;

            _db.Notifications.Update(e);
            await _db.SaveChangesAsync(ct);
        }


        // RescheduleAsync snippet
        public async Task RescheduleAsync(Guid notificationId, DateTime nextRun, CancellationToken ct = default)
        {
            var e = await _db.Notifications!.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);
            if (e == null) return;

            e.ScheduledFor = nextRun;
            e.Status = SendStatus.Pending.ToString();
            _db.Notifications.Update(e);
            await _db.SaveChangesAsync(ct);
        }

        public async Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default)
        {
            var entity = await _db.Notifications!.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);
            if (entity == null) return;

            var dead = new DeadLetterEntity
            {
                DeadId = Guid.NewGuid(),
                NotificationId = entity.NotificationId,
                Reason = reason,
                Attempts = entity.Attempts,
                LastError = reason,
                CreatedAt = DateTime.UtcNow,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    channel = entity.Channel,
                    recipient = entity.Recipient,
                    subject = entity.Subject,
                    body = entity.Body,
                    variables = entity.VariablesJson,
                    metadata = entity.MetadataJson
                })
            };

            entity.Status = "deadletter";
            _db.DeadLetters!.Add(dead);
            _db.Notifications.Update(entity);

            await _db.SaveChangesAsync(ct);
        }

        #endregion

        #region ICommQueue

        public async Task EnqueueAsync(NotificationDto notification, CancellationToken ct = default)
        {
            await EnqueueNotificationAsync(notification, ct);
        }

        public async Task<NotificationDto?> DequeueAsync(CancellationToken ct = default)
        {
            var list = await DequeueInternalAsync(1, ct);
            return list.FirstOrDefault();
        }

        public async Task AcknowledgeAsync(Guid notificationId, SendResultDto result, CancellationToken ct = default)
        {
            var audit = new CommAuditEntity
            {
                AuditId = Guid.NewGuid(),
                NotificationId = notificationId,
                Channel = result.Status.ToString(),
                Recipient = null,
                Status = result.Status.ToString(),
                ProviderMessageId = result.ProviderMessageId,
                ErrorMessage = result.ErrorMessage,
                Timestamp = result.Timestamp,
                PayloadJson = JsonSerializer.Serialize(result)
            };

            await _db.CommAudit!.AddAsync(audit, ct);

            var entity = await _db.Notifications!.FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);
            if (entity != null)
            {
                entity.Attempts = result.Attempt;
                entity.ProviderMessageId = result.ProviderMessageId;
                entity.Status = result.Status.ToString();
                entity.LastAttemptAt = DateTime.UtcNow;
                _db.Notifications.Update(entity);
            }

            await _db.SaveChangesAsync(ct);
        }

        #endregion

        #region Internal helpers

        private async Task<IEnumerable<NotificationDto>> DequeueInternalAsync(int max, CancellationToken ct)
        {
            if (max <= 0) max = 10;
            var now = DateTime.UtcNow;

            var candidates = await _db.Notifications!
                .Where(n => n.Status == SendStatus.Pending.ToString()
                            && (n.ScheduledFor == null || n.ScheduledFor <= now) )
                .OrderByDescending(n => n.Priority)
                .ThenBy(n => n.CreatedAt)
                .Take(max)
                .ToListAsync(ct);

            if (candidates.Count == 0) return Array.Empty<NotificationDto>();

            var result = new List<NotificationDto>(candidates.Count);

            foreach (var c in candidates)
            {
                c.Status = SendStatus.Processing.ToString();
                c.Attempts = c.Attempts + 1;
                c.LastAttemptAt = now;

                result.Add(MapToDto(c));
            }

            _db.Notifications.UpdateRange(candidates);
            await _db.SaveChangesAsync(ct);

            return result;
        }

        private static NotificationDto MapToDto(NotificationEntity e)
        {
            IDictionary<string, object?>? vars = null;
            if (!string.IsNullOrWhiteSpace(e.VariablesJson))
            {
                try { vars = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.VariablesJson); }
                catch { vars = null; }
            }

            IDictionary<string, string>? meta = null;
            if (!string.IsNullOrWhiteSpace(e.MetadataJson))
            {
                try { meta = JsonSerializer.Deserialize<Dictionary<string, string>>(e.MetadataJson); }
                catch { meta = null; }
            }

            ChannelType channel = ChannelType.Email;
            if (!string.IsNullOrWhiteSpace(e.Channel) && Enum.TryParse<ChannelType>(e.Channel, true, out var ch)) channel = ch;

            return new NotificationDto
            {
                NotificationId = e.NotificationId,
                Channel = channel,
                Recipient = e.Recipient,
                RecipientDisplayName = null,
                Subject = e.Subject,
                Body = e.Body,
                Template = null,
                Variables = vars,
                Priority = (Priority)Math.Min(byte.MaxValue, e.Priority),
                CreatedAt = e.CreatedAt,
                ScheduledFor = e.ScheduledFor,
                CorrelationId = null,
                Metadata = meta
            };
        }

        #endregion
    }
}
