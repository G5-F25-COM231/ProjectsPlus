using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Audit;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public sealed class AuditRepository : IAuditRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public AuditRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task WriteAsync(ProjectAudit audit, CancellationToken ct = default)
        {
            if (audit == null) throw new ArgumentNullException(nameof(audit));

            // Ensure timestamp is set deterministically if caller didn't set it
            if (audit.CreatedAt == default) audit.CreatedAt = DateTimeOffset.UtcNow;

            // Persist as a new row; keep it simple and idempotent on caller side
            await _db.ProjectAudits.AddAsync(audit, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Implement the exact interface signature including CancellationToken
        public async Task RecordAuthorizationAuditAsync(long? actorUserId, string action, string outcome, string? detail = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("action is required", nameof(action));
            if (string.IsNullOrWhiteSpace(outcome)) throw new ArgumentException("outcome is required", nameof(outcome));

            var entity = new AuditEntryEntity
            {
                TimestampUtc = DateTime.UtcNow,
                ActorUserId = actorUserId,
                Action = action,
                Outcome = outcome,
                Detail = detail
            };

            _db.AuditEntries.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

    }

}
