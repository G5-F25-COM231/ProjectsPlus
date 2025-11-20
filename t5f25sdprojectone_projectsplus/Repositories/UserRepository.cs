using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly IPasswordHasher<UserEntity> _passwordHasher;

        //public UserRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public UserRepository(ProjectsPlusDbContext db, IPasswordHasher<UserEntity> passwordHasher)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        }

        public async Task<UserEntity> InsertAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.Email = user.Email?.Trim();
            user.CreatedAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = user.CreatedAt;
            user.Version = 1;

            _db.Users.Add(user);
            try
            {
                await _db.SaveChangesAsync(ct);
                return user;
            }
            catch (DbUpdateException ex)
            {
                // map common unique constraint violations to DomainConflictException
                throw MapDbUpdateExceptionToDomainConflict(ex, $"A user with email '{user.Email}' already exists.");
            }
        }

        public async Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.Users.FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);
        }

        public async Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var normalized = email.Trim();
            return await _db.Users.FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted, ct);
        }

        public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            // optimistic concurrency using Version in where clause
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE [User]
            SET DisplayName = {user.DisplayName},
                AttributesJson = {user.AttributesJson},
                ProviderId = {user.ProviderId},
                IsActive = {user.IsActive},
                UpdatedAt = {now},
                Version = Version + 1
            WHERE Id = {user.Id} AND Version = {user.Version} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Update failed due to version mismatch for user {user.Id}.");

            // reload and return current row
            var updated = await _db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct);
            return updated;
        }

        public async Task DeleteAsync(long userId, int expectedVersion, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE [User]
            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
            WHERE Id = {userId} AND Version = {expectedVersion} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing user {userId}.");
        }

        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
        {
            // Provider-specific inspection could be added (SqlException.Number, SqliteException.SqliteErrorCode, etc.)
            // For now, best-effort: if inner exception mentions UNIQUE or duplicate, map to conflict
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                return new DomainConflictException(fallbackMessage);
            }

            // otherwise rethrow original exception
            return ex;
        }


        // using directives assumed: Microsoft.EntityFrameworkCore; System; System.Linq; System.Threading;
        public async Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            var skip = (page - 1) * pageSize;
            return await _db.Users
                .AsNoTracking()
                .Where(u => !u.IsDeleted)
                .OrderBy(u => u.Email)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        public async Task<bool> VerifyPasswordAsync(long userId, string plainTextPassword, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(plainTextPassword)) return false;

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
                .ConfigureAwait(false);

            if (user == null) return false;
            if (string.IsNullOrEmpty(user.PasswordHash)) return false;

            // Use ASP.NET Identity IPasswordHasher semantics to verify
            var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, plainTextPassword);

            // Accept PasswordVerificationResult.Success or SuccessRehashNeeded as valid
            return verificationResult == PasswordVerificationResult.Success
                || verificationResult == PasswordVerificationResult.SuccessRehashNeeded;
        }

        // Helper: set/replace password hash (useful elsewhere when creating/updating users)
        public Task<string> HashPasswordAsync(UserEntity user, string plainTextPassword)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(plainTextPassword)) throw new ArgumentException("password required", nameof(plainTextPassword));

            // IPasswordHasher is synchronous by design; keep signature sync-like but return Task for parity
            var hash = _passwordHasher.HashPassword(user, plainTextPassword);
            return Task.FromResult(hash);
        }

        public async Task<UserEntity> AddRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (user == null) throw new DomainNotFoundException($"User {userId} not found.");
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.Version += 1;
            await _db.SaveChangesAsync(ct);
            return user;
        }

        public async Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (user == null) throw new DomainNotFoundException($"User {userId} not found.");
            user.UpdatedAt = DateTimeOffset.UtcNow;
            user.Version += 1;
            await _db.SaveChangesAsync(ct);
            return user;
        }


    }
}
