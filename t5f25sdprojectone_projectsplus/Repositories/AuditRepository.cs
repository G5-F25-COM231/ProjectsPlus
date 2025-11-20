using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
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
            if (audit.Timestamp == default) audit.Timestamp = DateTimeOffset.UtcNow;

            // Persist as a new row; keep it simple and idempotent on caller side
            await _db.ProjectAudits.AddAsync(audit, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
