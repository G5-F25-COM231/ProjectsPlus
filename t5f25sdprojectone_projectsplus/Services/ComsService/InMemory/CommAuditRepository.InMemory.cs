// src/ProjectsPlus.Comms/Persistence/InMemory/CommAuditRepository.InMemory.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    /// <summary>
    /// In-memory CommAudit repository for local development and tests.
    /// Thread-safe and self-contained.
    /// </summary>
    //public interface ICommAuditRepository
    //{
    //    Task AddAsync(CommAuditDto audit, CancellationToken ct = default);
    //    Task<IReadOnlyList<CommAuditDto>> ListByNotificationIdAsync(Guid notificationId, int limit = 100, CancellationToken ct = default);
    //    Task<IReadOnlyList<CommAuditDto>> ListRecentAsync(int limit = 100, CancellationToken ct = default);
    //    Task<CommAuditDto?> GetByIdAsync(Guid auditId, CancellationToken ct = default);
    //}

    public class CommAuditRepositoryInMemory : ICommAuditRepository
    {
        // keyed by AuditId
        private readonly ConcurrentDictionary<Guid, CommAuditEntity> _store = new();

        public Task AddAsync(CommAuditDto audit, CancellationToken ct = default)
        {
            if (audit == null) throw new ArgumentNullException(nameof(audit));

            var id = audit.AuditId == Guid.Empty ? Guid.NewGuid() : audit.AuditId;
            var entity = new CommAuditEntity
            {
                AuditId = id,
                NotificationId = audit.NotificationId,
                Channel = audit.Channel,
                Recipient = audit.Recipient,
                Status = audit.Status,
                ProviderMessageId = audit.ProviderMessageId,
                ErrorMessage = audit.ErrorMessage,
                Timestamp = audit.Timestamp == default ? DateTime.UtcNow : audit.Timestamp,
                PayloadJson = audit.Payload == null ? null : JsonSerializer.Serialize(audit.Payload)
            };

            _store[entity.AuditId] = entity;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CommAuditDto>> ListByNotificationIdAsync(Guid notificationId, int limit = 100, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 100;

            var items = _store.Values
                .Where(a => a.NotificationId == notificationId)
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(MapToDto)
                .ToList();

            return Task.FromResult((IReadOnlyList<CommAuditDto>)items);
        }

        public Task<IReadOnlyList<CommAuditDto>> ListRecentAsync(int limit = 100, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 100;

            var items = _store.Values
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(MapToDto)
                .ToList();

            return Task.FromResult((IReadOnlyList<CommAuditDto>)items);
        }

        public Task<CommAuditDto?> GetByIdAsync(Guid auditId, CancellationToken ct = default)
        {
            if (auditId == Guid.Empty) return Task.FromResult<CommAuditDto?>(null);

            if (_store.TryGetValue(auditId, out var e))
                return Task.FromResult<CommAuditDto?>(MapToDto(e));

            return Task.FromResult<CommAuditDto?>(null);
        }

        private static CommAuditDto MapToDto(CommAuditEntity e)
        {
            IDictionary<string, object?>? payload = null;
            if (!string.IsNullOrWhiteSpace(e.PayloadJson))
            {
                try { payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.PayloadJson); }
                catch { payload = null; }
            }

            return new CommAuditDto
            {
                AuditId = e.AuditId,
                NotificationId = e.NotificationId,
                Channel = e.Channel,
                Recipient = e.Recipient,
                Status = e.Status,
                ProviderMessageId = e.ProviderMessageId,
                ErrorMessage = e.ErrorMessage,
                Timestamp = e.Timestamp,
                Payload = payload
            };
        }

        public Task AddAsync(Repositories.CommAuditDto audit, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        Task<IReadOnlyList<Repositories.CommAuditDto>> ICommAuditRepository.ListByNotificationIdAsync(Guid notificationId, int limit, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        Task<IReadOnlyList<Repositories.CommAuditDto>> ICommAuditRepository.ListRecentAsync(int limit, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        Task<Repositories.CommAuditDto?> ICommAuditRepository.GetByIdAsync(Guid auditId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }

    #region In-memory entity & DTO placeholders

    //internal sealed class CommAuditEntity
    //{
    //    public Guid AuditId { get; set; }
    //    public Guid? NotificationId { get; set; }
    //    public string? Channel { get; set; }
    //    public string? Recipient { get; set; }
    //    public string? Status { get; set; }
    //    public string? ProviderMessageId { get; set; }
    //    public string? ErrorMessage { get; set; }
    //    public DateTime Timestamp { get; set; }
    //    public string? PayloadJson { get; set; }
    //}

    public sealed class CommAuditDto
    {
        public Guid AuditId { get; set; } = Guid.Empty;
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public string? Recipient { get; set; }
        public string? Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public IDictionary<string, object?>? Payload { get; set; }
    }

    #endregion
}
