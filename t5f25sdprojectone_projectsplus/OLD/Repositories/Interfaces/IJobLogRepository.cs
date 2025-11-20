using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Jobs;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IJobLogRepository
    {
        /// <summary>
        /// Insert a new job log entry. Returns persisted entity with Id, Version and timestamps.
        /// Implementations should validate required fields and map DB unique/index violations to DomainConflictException.
        /// </summary>
        Task<JobLogEntity> InsertAsync(JobLogEntity entry, CancellationToken ct = default);

        /// <summary>
        /// Find by id (returns null if not found or soft-deleted).
        /// </summary>
        Task<JobLogEntity> FindByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// List job log entries for a job instance id, ordered newest first.
        /// </summary>
        Task<IReadOnlyList<JobLogEntity>> ListByJobInstanceIdAsync(string jobInstanceId, CancellationToken ct = default);

        /// <summary>
        /// Update an existing job log entry using optimistic concurrency via Version. On mismatch throw DomainConcurrencyException.
        /// </summary>
        Task<JobLogEntity> UpdateAsync(JobLogEntity entry, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete entry (set IsDeleted=true) using optimistic concurrency.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);
    }
}
