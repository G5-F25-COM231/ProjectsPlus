// src/Repositories/Interfaces/IAuthRepository.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Minimal repository surface for auth persistence operations.
    /// Implementations store users and refresh tokens.
    /// </summary>
    public interface IAuthRepository
    {
        Task<UserEntity?> FindByUsernameAsync(string username, CancellationToken ct = default);
        Task<UserEntity?> FindByIdAsync(long id, CancellationToken ct = default);
        Task PersistRefreshTokenAsync(long userId, string refreshToken, DateTime expiresAtUtc, CancellationToken ct = default);
        Task<RefreshTokenEntity?> FindRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
        Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken ct = default);
        Task RevokeAllRefreshTokensForUserAsync(long userId, CancellationToken ct = default);
    }
}
