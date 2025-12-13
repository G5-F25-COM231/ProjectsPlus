using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Persistence boundary for user data. Repository is responsible for persistence concerns:
    /// - validation that depends on DB constraints (uniqueness) should map to DomainConflictException
    /// - optimistic concurrency via Version and soft-delete semantics
    /// - password hashing and verification may be implemented here or delegated to a lower-level store
    /// </summary>
    public interface IUserRepository
    {
        Task<UserEntity> InsertAsync(UserEntity user, CancellationToken ct = default);

        Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default);

        Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default);

        Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);

        Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default);

        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// Verify a plain-text password against the stored credentials for a user.
        /// Return true when the password matches; false otherwise.
        /// Implementations must use timing-safe comparisons.
        /// </summary>
        Task<bool> VerifyPasswordAsync(long userId, string plainTextPassword, CancellationToken ct = default);

        /// <summary>
        /// Add a role to the user (idempotent). Returns updated user.
        /// </summary>
        Task<UserEntity> AddRoleAsync(long userId, string role, CancellationToken ct = default);

        /// <summary>
        /// Remove a role from the user (idempotent). Returns updated user.
        /// </summary>
        Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default);
    }
}
