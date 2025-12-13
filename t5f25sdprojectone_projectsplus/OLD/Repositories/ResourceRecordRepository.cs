//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.EntityFrameworkCore;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.Models;
//using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

//namespace t5f25sdprojectone_projectsplus.Repositories
//{
//    public class ResourceRecordRepository : Interfaces.IResourceRecordRepository
//    {
//        private readonly ProjectsPlusDbContext _db;

//        public ResourceRecordRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

//        public async Task<ResourceRecordEntity> InsertAsync(ResourceRecordEntity entity, CancellationToken ct = default)
//        {
//            if (entity == null) throw new ArgumentNullException(nameof(entity));

//            entity.Provider = entity.Provider?.Trim();
//            entity.ProviderResourceId = entity.ProviderResourceId?.Trim();
//            entity.CreatedAt = DateTimeOffset.UtcNow;
//            entity.UpdatedAt = entity.CreatedAt;
//            entity.Version = 1;
//            entity.IsDeleted = false;

//            _db.ResourceRecords.Add(entity);
//            try
//            {
//                await _db.SaveChangesAsync(ct);
//                return entity;
//            }
//            catch (DbUpdateException ex)
//            {
//                throw MapDbUpdateExceptionToDomainConflict(ex, $"A resource record for provider '{entity.Provider}' and id '{entity.ProviderResourceId}' already exists.");
//            }
//        }

//        public async Task<ResourceRecordEntity> FindByIdAsync(long id, CancellationToken ct = default)
//        {
//            return await _db.ResourceRecords.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct);
//        }

//        public async Task<ResourceRecordEntity> FindByProviderAsync(string provider, string providerResourceId, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerResourceId)) return null;
//            var p = provider.Trim();
//            var pid = providerResourceId.Trim();
//            return await _db.ResourceRecords.FirstOrDefaultAsync(r => r.Provider == p && r.ProviderResourceId == pid && !r.IsDeleted, ct);
//        }

//        public async Task<ResourceRecordEntity> UpdateAsync(ResourceRecordEntity entity, CancellationToken ct = default)
//        {
//            if (entity == null) throw new ArgumentNullException(nameof(entity));

//            var now = DateTimeOffset.UtcNow;

//            // Use raw SQL update to enforce optimistic concurrency atomically
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE ResourceRecords
//            SET ProfileJson = {entity.ProfileJson},
//                CorrelationId = {entity.CorrelationId},
//                ProjectId = {entity.ProjectId},
//                WorkspaceId = {entity.WorkspaceId},
//                RelatedToProjectId = {entity.RelatedToProjectId},
//                SystemTypeId = {entity.SystemTypeId},
//                IsDeleted = {entity.IsDeleted},
//                UpdatedAt = {now},
//                Version = Version + 1
//            WHERE Id = {entity.Id} AND Version = {entity.Version}
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Update failed due to version mismatch for resource record {entity.Id}.");

//            // reload and return current row
//            var updated = await _db.ResourceRecords.FirstOrDefaultAsync(r => r.Id == entity.Id, ct);
//            return updated;
//        }

//        public async Task<IReadOnlyList<ResourceRecordEntity>> ListByProjectIdAsync(long projectId, CancellationToken ct = default)
//        {
//            var list = await _db.ResourceRecords
//                .AsNoTracking()
//                .Where(r => r.ProjectId == projectId && !r.IsDeleted)
//                .OrderByDescending(r => r.UpdatedAt)
//                .ToListAsync(ct);

//            return list;
//        }

//        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
//        {
//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE ResourceRecords
//            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
//            WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing resource record {id}.");
//        }

//        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
//        {
//            var inner = ex.InnerException?.Message ?? ex.Message;
//            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
//            {
//                return new DomainConflictException(fallbackMessage);
//            }
//            return ex;
//        }
//    }
//}
