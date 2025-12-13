// src/Repositories/EF/AuthRepository.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class AuthRepository : IAuthRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public AuthRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<UserEntity?> FindByUsernameAsync(string username, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            return await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == username, ct).ConfigureAwait(false);
        }

        public async Task<UserEntity?> FindByIdAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            return await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct).ConfigureAwait(false);
        }

        public async Task PersistRefreshTokenAsync(long userId, string refreshToken, DateTime expiresAtUtc, CancellationToken ct = default)
        {
            var entity = new RefreshTokenEntity
            {
                Token = refreshToken,
                UserId = userId,
                ExpiresAtUtc = expiresAtUtc,
                IsRevoked = false,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.RefreshTokens.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<RefreshTokenEntity?> FindRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return null;
            return await _db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(rt => rt.Token == refreshToken, ct).ConfigureAwait(false);
        }

        public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            var rt = await _db.RefreshTokens.FirstOrDefaultAsync(x => x.Token == refreshToken, ct).ConfigureAwait(false);
            if (rt == null) return;
            rt.IsRevoked = true;
            rt.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task RevokeAllRefreshTokensForUserAsync(long userId, CancellationToken ct = default)
        {
            var tokens = await _db.RefreshTokens.Where(x => x.UserId == userId && !x.IsRevoked).ToListAsync(ct).ConfigureAwait(false);
            if (tokens.Count == 0) return;
            foreach (var t in tokens)
            {
                t.IsRevoked = true;
                t.RevokedAtUtc = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
