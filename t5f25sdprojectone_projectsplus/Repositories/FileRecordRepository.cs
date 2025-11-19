using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class FileRecordRepository : Interfaces.IFileRecordRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public FileRecordRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<FileRecordEntity> InsertAsync(FileRecordEntity entity, CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            entity.StorageKey = entity.StorageKey?.Trim();
            entity.ChecksumSha256 = entity.ChecksumSha256?.Trim()?.ToLowerInvariant();
            entity.CreatedAt = DateTimeOffset.UtcNow;
            entity.UpdatedAt = entity.CreatedAt;
            entity.Version = 1;
            entity.IsDeleted = false;
            entity.ScanStatus = entity.ScanStatus; // default set by model

            _db.FileRecords.Add(entity);
            try
            {
                await _db.SaveChangesAsync(ct);
                return entity;
            }
            catch (DbUpdateException ex)
            {
                throw MapDbUpdateExceptionToDomainConflict(ex, $"A file with the same storage key or checksum already exists.");
            }
        }

        public async Task<FileRecordEntity> FindByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.FileRecords.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);
        }

        public async Task<FileRecordEntity> FindByChecksumAsync(string checksumSha256, long ownerUserId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(checksumSha256)) return null;
            var cs = checksumSha256.Trim().ToLowerInvariant();
            return await _db.FileRecords.FirstOrDefaultAsync(f => f.ChecksumSha256 == cs && f.OwnerUserId == ownerUserId && !f.IsDeleted, ct);
        }

        public async Task<FileRecordEntity> UpdateAsync(FileRecordEntity entity, CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var now = DateTimeOffset.UtcNow;

            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE FileRecords
            SET ProjectId = {entity.ProjectId},
                StorageKey = {entity.StorageKey},
                ChecksumSha256 = {entity.ChecksumSha256},
                SizeBytes = {entity.SizeBytes},
                MimeType = {entity.MimeType},
                OriginalFileName = {entity.OriginalFileName},
                ScanStatus = {(int)entity.ScanStatus},
                QuarantineReason = {entity.QuarantineReason},
                UpdatedAt = {now},
                Version = Version + 1
            WHERE Id = {entity.Id} AND Version = {entity.Version} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Update failed due to version mismatch for file record {entity.Id}.");

            var updated = await _db.FileRecords.FirstOrDefaultAsync(f => f.Id == entity.Id, ct);
            return updated;
        }

        public async Task<IReadOnlyList<FileRecordEntity>> ListByProjectIdAsync(long projectId, CancellationToken ct = default)
        {
            var list = await _db.FileRecords
                .AsNoTracking()
                .Where(f => f.ProjectId == projectId && !f.IsDeleted)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync(ct);

            return list;
        }

        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE FileRecords
            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
            WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing file record {id}.");
        }

        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                return new DomainConflictException(fallbackMessage);
            }
            return ex;
        }
    }
}
