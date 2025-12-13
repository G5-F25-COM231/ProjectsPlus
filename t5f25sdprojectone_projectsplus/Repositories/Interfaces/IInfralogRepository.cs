using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IInfralogRepository
    {
        /// <summary>
        /// Insert a new infrastructure log entry. Returns persisted entity with Id and Version.
        /// Implementations should validate required fields and map DB unique/index violations to DomainConflictException.
        /// </summary>
        Task<InfralogEntity> InsertAsync(InfralogEntity entry, CancellationToken ct = default);

        /// <summary>
        /// Find by id (returns null if not found).
        /// </summary>
        Task<InfralogEntity> FindByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// List entries for a correlation id, ordered newest first.
        /// </summary>
        Task<IReadOnlyList<InfralogEntity>> ListByCorrelationIdAsync(string correlationId, CancellationToken ct = default);

        /// <summary>
        /// Update an existing log entry using optimistic concurrency via Version. On mismatch throw DomainConcurrencyException.
        /// </summary>
        Task<InfralogEntity> UpdateAsync(InfralogEntity entry, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete entry (set IsDeleted=true) using optimistic concurrency.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);
    }
}
