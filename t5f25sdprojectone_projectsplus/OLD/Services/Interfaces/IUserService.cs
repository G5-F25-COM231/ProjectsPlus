using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Application-level user service boundary.
    /// Service implementations are responsible for validation, authorization checks, audit logging,
    /// and mapping domain errors (conflicts, concurrency) to appropriate service-level exceptions.
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Create a new user. Returns the persisted user entity with Id, timestamps and Version.
        /// Throws ArgumentException for validation failures and DomainConflictException for uniqueness violations.
        /// </summary>
        Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>
        /// Find a user by numeric Id. Returns null if not found or soft-deleted.
        /// </summary>
        Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find a user by email (case-insensitive). Returns null if not found or soft-deleted.
        /// </summary>
        Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default);

        /// <summary>
        /// List users with optional paging. Implementations may support filtering in extended overloads.
        /// </summary>
        Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);

        /// <summary>
        /// Update an existing user using optimistic concurrency. On version mismatch throw DomainConcurrencyException.
        /// Returns the updated entity.
        /// </summary>
        Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete a user by Id using optimistic concurrency. On mismatch throw DomainConcurrencyException.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// Verify credentials (email + password) and return the user if successful; otherwise null.
        /// Implementations must handle hashing and timing-safe comparisons.
        /// </summary>
        Task<UserEntity> AuthenticateAsync(string email, string password, CancellationToken ct = default);

        /// <summary>
        /// Assign a role to a user. Idempotent; returns updated user.
        /// </summary>
        Task<UserEntity> AssignRoleAsync(long userId, string role, CancellationToken ct = default);

        /// <summary>
        /// Remove a role from a user. Idempotent; returns updated user.
        /// </summary>
        Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default);
    }
}
