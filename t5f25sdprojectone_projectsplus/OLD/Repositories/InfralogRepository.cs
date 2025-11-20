//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.EntityFrameworkCore;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.Models;
//using t5f25sdprojectone_projectsplus.Repositories;

//namespace t5f25sdprojectone_projectsplus.Repositories.EntityFramework
//{
//    public class InfralogRepository : Repositories.Interfaces.IInfralogRepository
//    {
//        private readonly ProjectsPlusDbContext _db;

//        public InfralogRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

//        public async Task<InfralogEntity> InsertAsync(InfralogEntity entry, CancellationToken ct = default)
//        {
//            if (entry == null) throw new ArgumentNullException(nameof(entry));
//            // Basic validation
//            if (string.IsNullOrWhiteSpace(entry.CorrelationId)) throw new ArgumentException("CorrelationId is required", nameof(entry));
//            if (string.IsNullOrWhiteSpace(entry.Category)) throw new ArgumentException("Category is required", nameof(entry));
//            if (string.IsNullOrWhiteSpace(entry.Message)) throw new ArgumentException("Message is required", nameof(entry));

//            entry.CorrelationId = entry.CorrelationId.Trim();
//            entry.Category = entry.Category.Trim();
//            entry.Message = entry.Message.Trim();
//            entry.CreatedAt = DateTimeOffset.UtcNow;
//            entry.UpdatedAt = entry.CreatedAt;
//            entry.Version = 1;
//            entry.IsDeleted = false;

//            _db.Infralogs.Add(entry);
//            try
//            {
//                await _db.SaveChangesAsync(ct);
//                return entry;
//            }
//            catch (DbUpdateException ex)
//            {
//                // No obvious unique index expected here, but map conflict generically
//                throw MapDbUpdateExceptionToDomainConflict(ex, "Infralog insert conflict.");
//            }
//        }

//        public async Task<InfralogEntity> FindByIdAsync(long id, CancellationToken ct = default)
//        {
//            return await _db.Infralogs.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
//        }

//        public async Task<IReadOnlyList<InfralogEntity>> ListByCorrelationIdAsync(string correlationId, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(correlationId)) return Array.Empty<InfralogEntity>();
//            var c = correlationId.Trim();
//            return await _db.Infralogs
//                .AsNoTracking()
//                .Where(x => x.CorrelationId == c && !x.IsDeleted)
//                .OrderByDescending(x => x.CreatedAt)
//                .ToListAsync(ct);
//        }

//        public async Task<InfralogEntity> UpdateAsync(InfralogEntity entry, CancellationToken ct = default)
//        {
//            if (entry == null) throw new ArgumentNullException(nameof(entry));

//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE Infralogs
//            SET Category = {entry.Category},
//                Message = {entry.Message},
//                DetailsJson = {entry.DetailsJson},
//                ActorUserId = {entry.ActorUserId},
//                UpdatedAt = {now},
//                Version = Version + 1
//            WHERE Id = {entry.Id} AND Version = {entry.Version} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Update failed due to version mismatch for infralog {entry.Id}.");

//            var updated = await _db.Infralogs.FirstOrDefaultAsync(x => x.Id == entry.Id, ct);
//            return updated;
//        }

//        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
//        {
//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE Infralogs
//            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
//            WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing infralog {id}.");
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
