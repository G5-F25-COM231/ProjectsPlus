using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IResourceRecordRepository
    {
        /// <summary>
        /// Insert a new resource record. On success returns persisted entity.
        /// If a uniqueness violation (provider + providerResourceId) occurs, throw DomainConflictException.
        /// </summary>
        Task<ResourceRecordEntity> InsertAsync(ResourceRecordEntity entity, CancellationToken ct = default);

        /// <summary>
        /// Find by internal id. Returns null if not found or soft-deleted.
        /// </summary>
        Task<ResourceRecordEntity> FindByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find by provider and providerResourceId. Returns null if not found or soft-deleted.
        /// </summary>
        Task<ResourceRecordEntity> FindByProviderAsync(string provider, string providerResourceId, CancellationToken ct = default);

        /// <summary>
        /// Update an existing record using optimistic concurrency (Version). Returns updated entity.
        /// On version mismatch throw DomainConcurrencyException.
        /// On unique constraint violation throw DomainConflictException.
        /// </summary>
        Task<ResourceRecordEntity> UpdateAsync(ResourceRecordEntity entity, CancellationToken ct = default);

        /// <summary>
        /// List resource records linked to a project. Returns empty list if none.
        /// </summary>
        Task<IReadOnlyList<ResourceRecordEntity>> ListByProjectIdAsync(long projectId, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete resource record (set IsDeleted=true) using optimistic concurrency.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);
    }
}
