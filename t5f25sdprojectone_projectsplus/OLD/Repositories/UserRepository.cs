//using System;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.EntityFrameworkCore;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.Models;
//using t5f25sdprojectone_projectsplus.Models.Users;
//using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

//namespace t5f25sdprojectone_projectsplus.Repositories.EntityFramework
//{
//    public class UserRepository : IUserRepository
//    {
//        private readonly ProjectsPlusDbContext _db;

//        public UserRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

//        public async Task<UserEntity> InsertAsync(UserEntity user, CancellationToken ct = default)
//        {
//            if (user == null) throw new ArgumentNullException(nameof(user));
//            user.Email = user.Email?.Trim();
//            user.CreatedAt = DateTimeOffset.UtcNow;
//            user.UpdatedAt = user.CreatedAt;
//            user.Version = 1;

//            _db.Users.Add(user);
//            try
//            {
//                await _db.SaveChangesAsync(ct);
//                return user;
//            }
//            catch (DbUpdateException ex)
//            {
//                // map common unique constraint violations to DomainConflictException
//                throw MapDbUpdateExceptionToDomainConflict(ex, $"A user with email '{user.Email}' already exists.");
//            }
//        }

//        public async Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default)
//        {
//            return await _db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
//        }

//        public async Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(email)) return null;
//            var normalized = email.Trim();
//            return await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted, ct);
//        }

//        public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
//        {
//            if (user == null) throw new ArgumentNullException(nameof(user));
//            // optimistic concurrency using Version in where clause
//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE [User]
//            SET DisplayName = {user.DisplayName},
//                AttributesJson = {user.AttributesJson},
//                ProviderId = {user.ProviderId},
//                IsActive = {user.IsActive},
//                UpdatedAt = {now},
//                Version = Version + 1
//            WHERE Id = {user.Id} AND Version = {user.Version} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Update failed due to version mismatch for user {user.Id}.");

//            // reload and return current row
//            var updated = await _db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct);
//            return updated;
//        }

//        public async Task DeleteAsync(long userId, int expectedVersion, CancellationToken ct = default)
//        {
//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE [User]
//            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
//            WHERE Id = {userId} AND Version = {expectedVersion} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing user {userId}.");
//        }

//        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
//        {
//            // Provider-specific inspection could be added (SqlException.Number, SqliteException.SqliteErrorCode, etc.)
//            // For now, best-effort: if inner exception mentions UNIQUE or duplicate, map to conflict
//            var inner = ex.InnerException?.Message ?? ex.Message;
//            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
//            {
//                return new DomainConflictException(fallbackMessage);
//            }

//            // otherwise rethrow original exception
//            return ex;
//        }
//    }
//}
