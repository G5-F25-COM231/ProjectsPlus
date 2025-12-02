// src/ProjectsPlus.Comms/Persistence/NotificationRepository.InMemory.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    // In-memory implementation of INotificationRepository and ICommQueue for local development and tests.
    // This file is intentionally self-contained so you can drop it into the Comms project.
    public interface INotificationRepository
    {
        Task EnqueueNotificationAsync(NotificationDto note, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationDto>> DequeuePendingAsync(int max, CancellationToken ct = default);
        Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, CancellationToken ct = default);
        Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default);
    }

    public interface ICommQueue
    {
        Task EnqueueAsync(NotificationDto notification, CancellationToken ct = default);
        Task<NotificationDto?> DequeueAsync(CancellationToken ct = default);
        Task AcknowledgeAsync(Guid notificationId, SendResultDto result, CancellationToken ct = default);
        Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default);
    }

    public class NotificationRepositoryInMemory : INotificationRepository, ICommQueue
    {
        // notifications keyed by NotificationId
        private readonly ConcurrentDictionary<Guid, NotificationEntity> _notifications = new();
        // simple audit store
        private readonly ConcurrentDictionary<Guid, List<CommAuditEntity>> _audits = new();
        // dead letters keyed by DeadId
        private readonly ConcurrentDictionary<Guid, DeadLetterEntity> _deadLetters = new();

        // lightweight lock for claiming notifications
        private readonly SemaphoreSlim _claimLock = new(1, 1);

        public Task EnqueueNotificationAsync(NotificationDto note, CancellationToken ct = default)
        {
            if (note == null) throw new ArgumentNullException(nameof(note));

            var id = note.NotificationId == Guid.Empty ? Guid.NewGuid() : note.NotificationId;
            var entity = new NotificationEntity
            {
                NotificationId = id,
                Channel = note.Channel.ToString(),
                Recipient = note.Recipient ?? string.Empty,
                Subject = note.Subject,
                Body = note.Body,
                VariablesJson = note.Variables == null ? null : JsonSerializer.Serialize(note.Variables),
                Priority = (byte)Math.Clamp((int)note.Priority, 0, byte.MaxValue),
                Status = SendStatus.Pending.ToString(),
                Attempts = 0,
                ScheduledFor = note.ScheduledFor,
                CreatedAt = note.CreatedAt == default ? DateTime.UtcNow : note.CreatedAt,
                MetadataJson = note.Metadata == null ? null : JsonSerializer.Serialize(note.Metadata)
            };

            _notifications[entity.NotificationId] = entity;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<NotificationDto>> DequeuePendingAsync(int max, CancellationToken ct = default)
        {
            return Task.FromResult((IReadOnlyList<NotificationDto>)DequeueInternalAsync(max).ToList());
        }

        public Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, CancellationToken ct = default)
        {
            if (_notifications.TryGetValue(notificationId, out var e))
            {
                e.Attempts = attempts;
                e.ProviderMessageId = providerMessageId;
                e.Status = status ?? e.Status;
                e.LastAttemptAt = DateTime.UtcNow;
                _notifications[notificationId] = e;
            }

            return Task.CompletedTask;
        }

        public Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default)
        {
            if (!_notifications.TryGetValue(notificationId, out var entity))
                return Task.CompletedTask;

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

            _deadLetters[dead.DeadId] = dead;

            // mark notification as deadletter
            entity.Status = "deadletter";
            _notifications[notificationId] = entity;

            return Task.CompletedTask;
        }

        // ICommQueue

        public Task EnqueueAsync(NotificationDto notification, CancellationToken ct = default)
        {
            return EnqueueNotificationAsync(notification, ct);
        }

        public Task<NotificationDto?> DequeueAsync(CancellationToken ct = default)
        {
            var list = DequeueInternalAsync(1);
            return Task.FromResult(list.FirstOrDefault());
        }

        public Task AcknowledgeAsync(Guid notificationId, SendResultDto result, CancellationToken ct = default)
        {
            // record audit
            var audit = new CommAuditEntity
            {
                AuditId = Guid.NewGuid(),
                NotificationId = notificationId,
                Channel = result.Channel.ToString(),
                Recipient = result.Recipient,
                Status = result.Status.ToString(),
                ProviderMessageId = result.ProviderMessageId,
                ErrorMessage = result.ErrorMessage,
                Timestamp = result.Timestamp == default ? DateTime.UtcNow : result.Timestamp,
                PayloadJson = JsonSerializer.Serialize(result)
            };

            var list = _audits.GetOrAdd(notificationId, _ => new List<CommAuditEntity>());
            lock (list)
            {
                list.Add(audit);
            }

            // update notification state
            if (_notifications.TryGetValue(notificationId, out var entity))
            {
                entity.Attempts = result.Attempt;
                entity.ProviderMessageId = result.ProviderMessageId;
                entity.Status = result.Status.ToString();
                entity.LastAttemptAt = DateTime.UtcNow;
                _notifications[notificationId] = entity;
            }

            return Task.CompletedTask;
        }

        // Internal helpers

        private IEnumerable<NotificationDto> DequeueInternalAsync(int max)
        {
            if (max <= 0) max = 10;
            var now = DateTime.UtcNow;

            // simple optimistic claim: lock briefly to avoid races in tests
            _claimLock.Wait();
            try
            {
                var candidates = _notifications.Values
                    .Where(n => string.Equals(n.Status, SendStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase)
                                && (n.ScheduledFor == null || n.ScheduledFor <= now))
                    .OrderByDescending(n => n.Priority)
                    .ThenBy(n => n.CreatedAt)
                    .Take(max)
                    .ToList();

                var result = new List<NotificationDto>(candidates.Count);

                foreach (var c in candidates)
                {
                    c.Status = SendStatus.Processing.ToString();
                    c.Attempts = c.Attempts + 1;
                    c.LastAttemptAt = now;
                    _notifications[c.NotificationId] = c;

                    result.Add(MapToDto(c));
                }

                return result;
            }
            finally
            {
                _claimLock.Release();
            }
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
    }

    #region In-memory entity placeholders

    // These mirror the EF entities but are lightweight for in-memory store
    internal sealed class NotificationEntity
    {
        public Guid NotificationId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? VariablesJson { get; set; }
        public byte Priority { get; set; }
        public string Status { get; set; } = SendStatus.Pending.ToString();
        public int Attempts { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? MetadataJson { get; set; }
    }

    internal sealed class CommAuditEntity
    {
        public Guid AuditId { get; set; }
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public string? Recipient { get; set; }
        public string? Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; }
        public string? PayloadJson { get; set; }
    }

    internal sealed class DeadLetterEntity
    {
        public Guid DeadId { get; set; }
        public Guid? NotificationId { get; set; }
        public string? Reason { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? PayloadJson { get; set; }
    }

    #endregion

    #region DTO / enum placeholders

    public sealed class NotificationDto
    {
        public Guid NotificationId { get; set; }
        public ChannelType Channel { get; set; } = ChannelType.Email;
        public string Recipient { get; set; } = string.Empty;
        public string? RecipientDisplayName { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? Template { get; set; }
        public IDictionary<string, object?>? Variables { get; set; }
        public Priority Priority { get; set; } = Priority.Normal;
        public DateTime CreatedAt { get; set; }
        public DateTime? ScheduledFor { get; set; }
        public string? CorrelationId { get; set; }
        public IDictionary<string, string>? Metadata { get; set; }
    }

    public sealed class SendResultDto
    {
        public Guid NotificationId { get; set; }
        public ChannelType Channel { get; set; }
        public string? Recipient { get; set; }
        public SendStatus Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Attempt { get; set; } = 1;
    }

    public enum ChannelType
    {
        Email,
        Webhook,
        InApp,
        Sms,
        Push
    }

    public enum Priority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public enum SendStatus
    {
        Pending,
        Processing,
        Sent,
        Failed,
        DeadLetter
    }

    #endregion
}
