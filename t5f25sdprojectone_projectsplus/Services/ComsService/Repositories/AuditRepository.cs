// src/ProjectsPlus.Comms/Persistence/CommAuditRepository.Ef.cs
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

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public interface ICommAuditRepository
    {
        Task AddAsync(CommAuditDto audit, CancellationToken ct = default);
        Task<IReadOnlyList<CommAuditDto>> ListByNotificationIdAsync(Guid notificationId, int limit = 100, CancellationToken ct = default);
        Task<IReadOnlyList<CommAuditDto>> ListRecentAsync(int limit = 100, CancellationToken ct = default);
        Task<CommAuditDto?> GetByIdAsync(Guid auditId, CancellationToken ct = default);
    }

    public class CommAuditRepository : ICommAuditRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public CommAuditRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task AddAsync(CommAuditDto audit, CancellationToken ct = default)
        {
            if (audit == null) throw new ArgumentNullException(nameof(audit));

            var entity = new CommAuditEntity
            {
                AuditId = audit.AuditId == Guid.Empty ? Guid.NewGuid() : audit.AuditId,
                NotificationId = audit.NotificationId,
                Channel = audit.Channel,
                Recipient = audit.Recipient,
                Status = audit.Status,
                ProviderMessageId = audit.ProviderMessageId,
                ErrorMessage = audit.ErrorMessage,
                Timestamp = audit.Timestamp == default ? DateTime.UtcNow : audit.Timestamp,
                PayloadJson = audit.Payload == null ? null : JsonSerializer.Serialize(audit.Payload)
            };

            await _db.CommAudit!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<CommAuditDto>> ListByNotificationIdAsync(Guid notificationId, int limit = 100, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 100;

            var items = await _db.CommAudit!
                .AsNoTracking()
                .Where(a => a.NotificationId == notificationId)
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToListAsync(ct);

            return items.Select(MapToDto).ToList();
        }

        public async Task<IReadOnlyList<CommAuditDto>> ListRecentAsync(int limit = 100, CancellationToken ct = default)
        {
            if (limit <= 0) limit = 100;

            var items = await _db.CommAudit!
                .AsNoTracking()
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToListAsync(ct);

            return items.Select(MapToDto).ToList();
        }

        public async Task<CommAuditDto?> GetByIdAsync(Guid auditId, CancellationToken ct = default)
        {
            var e = await _db.CommAudit!.AsNoTracking().FirstOrDefaultAsync(a => a.AuditId == auditId, ct);
            return e == null ? null : MapToDto(e);
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
    }

    #region DTO placeholders

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
