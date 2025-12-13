using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Jobs;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class JobLogRepository : IJobLogRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public JobLogRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<JobLogEntity> InsertAsync(JobLogEntity entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.JobId)) throw new ArgumentException("JobId is required", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.JobType)) throw new ArgumentException("JobType is required", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.ScopeKey)) throw new ArgumentException("ScopeKey is required", nameof(entry));
            if (entry.CorrelationId == Guid.Empty) throw new ArgumentException("CorrelationId is required", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Status)) throw new ArgumentException("Status is required", nameof(entry));

            entry.JobId = entry.JobId.Trim();
            entry.JobType = entry.JobType.Trim();
            entry.ScopeKey = entry.ScopeKey.Trim();
            entry.Status = entry.Status.Trim();
            entry.CreatedAt = DateTimeOffset.UtcNow;
            entry.UpdatedAt = entry.CreatedAt;
            entry.Version = 1;
            entry.IsDeleted = false;

            _db.JobLogs.Add(entry);
            try
            {
                await _db.SaveChangesAsync(ct);
                return entry;
            }
            catch (DbUpdateException ex)
            {
                var inner = ex.InnerException?.Message ?? ex.Message;
                if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
                    throw new DomainConflictException($"A job with the same scope and correlation id already exists (ScopeKey={entry.ScopeKey}).");
                throw;
            }
        }

        public async Task<JobLogEntity> FindByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.JobLogs.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        }

        public async Task<IReadOnlyList<JobLogEntity>> ListByJobInstanceIdAsync(string jobInstanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(jobInstanceId)) return Array.Empty<JobLogEntity>();
            var j = jobInstanceId.Trim();
            return await _db.JobLogs
                .AsNoTracking()
                .Where(x => x.JobId == j && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
        }

        public async Task<JobLogEntity> UpdateAsync(JobLogEntity entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            var now = DateTimeOffset.UtcNow;

            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE JobLogs
            SET Status = {entry.Status},
                Attempts = {entry.Attempts},
                StartedAt = {entry.StartedAt},
                FinishedAt = {entry.FinishedAt},
                OutcomeJson = {entry.OutcomeJson},
                OutcomeCode = {entry.OutcomeCode},
                Owner = {entry.Owner},
                UpdatedAt = {now},
                Version = Version + 1
            WHERE Id = {entry.Id} AND Version = {entry.Version} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Update failed due to version mismatch for job log {entry.Id}.");

            var updated = await _db.JobLogs.FirstOrDefaultAsync(x => x.Id == entry.Id, ct);
            return updated;
        }

        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE JobLogs
            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
            WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing job log {id}.");
        }
    }
}
