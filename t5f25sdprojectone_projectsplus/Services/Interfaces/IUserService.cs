using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.DTOs.UserDTOs;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Application-level user service boundary.
    /// Service implementations are responsible for validation, authorization checks, audit logging,
    /// and mapping domain errors (conflicts, concurrency) to appropriate service-level exceptions.
    /// Keep methods small and testable; prefer explicit result objects for operations that have
    /// business outcomes (idempotency, concurrency, partial success).
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Create a new user. Returns the persisted user entity with Id, timestamps and Version.
        /// Throws ArgumentException for validation failures and DomainConflictException for uniqueness violations.
        /// </summary>
        Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>
        /// Compatibility alias for CreateAsync used by some callers/tests.
        /// Implementations should forward to CreateAsync.
        /// </summary>
        Task<UserEntity> CreateUserAsync(UserEntity user, CancellationToken ct = default);

        /// <summary>
        /// Create a new user in an idempotent-friendly form using a request DTO.
        /// Implementations may forward to CreateAsync(UserEntity, CancellationToken).
        /// </summary>
        Task<CreateUserResult> CreateFromRequestAsync(CreateUserRequest request, string correlationId, CancellationToken ct = default);

        /// <summary>
        /// Find a user by numeric Id. Returns null if not found or soft-deleted.
        /// </summary>
        Task<UserEntity?> FindByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find a user by email (case-insensitive). Returns null if not found or soft-deleted.
        /// Preferred canonical name for implementations.
        /// </summary>
        Task<UserEntity?> FindByEmailAsync(string email, CancellationToken ct = default);

        /// <summary>
        /// Compatibility alias: some callers/tests expect GetByEmailAsync.
        /// Implementations should forward this to FindByEmailAsync.
        /// </summary>
        Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default);

        /// <summary>
        /// Return a presentation/profile object for the user suitable for API responses.
        /// Implementations should map domain UserEntity -> UserProfile and hide internal data.
        /// </summary>
        Task<UserProfile?> GetProfileAsync(long userId, CancellationToken ct = default);

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
        /// Patch user fields using a request object. Returns a PatchResult describing success,
        /// concurrency failures, or business validation failures.
        /// </summary>
        Task<PatchUserResult> PatchUserAsync(long userId, PatchUserRequest patch, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete a user by Id using optimistic concurrency. On mismatch throw DomainConcurrencyException.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// Compatibility alias for DeleteAsync used by some callers/tests.
        /// Implementations should forward to DeleteAsync and preserve semantics.
        /// </summary>
        Task DeleteUserAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// Verify credentials (email + password) and return the user if successful; otherwise null.
        /// Implementations must handle hashing and timing-safe comparisons.
        /// </summary>
        Task<UserEntity?> AuthenticateAsync(string email, string password, CancellationToken ct = default);

        /// <summary>
        /// Assign a role to a user. Idempotent; returns updated user.
        /// </summary>
        Task<UserEntity> AssignRoleAsync(long userId, string role, CancellationToken ct = default);

        /// <summary>
        /// Remove a role from a user. Idempotent; returns updated user.
        /// </summary>
        Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default);

        /// <summary>
        /// Domain-level bulk/apply operation. Caller must provide correlationId for idempotency and traceability.
        /// Returns a summary object containing success/failure details suitable for controller responses.
        /// </summary>
        Task<ApplyUsersResult> ApplyUsersAsync(ApplyUsersRequest request, string correlationId, CancellationToken ct = default);
    }

    #region DTOs / Result types used by the service contract

    /// <summary>
    /// Minimal DTO representing create request shape used by controllers.
    /// </summary>
    public record CreateUserRequest(string Email, string Password, string? DisplayName = null, string? FirstName = null, string? LastName = null);

    /// <summary>
    /// Result of CreateFromRequestAsync. Use Succeeded = false to indicate predictable business rejection.
    /// On success UserId will be populated.
    /// </summary>
    public record CreateUserResult(bool Succeeded, long? UserId, string? Reason);

    /// <summary>
    /// Patch request shape for user updates.
    /// </summary>
    public record PatchUserRequest(string? DisplayName = null, string? FirstName = null, string? LastName = null, int? ExpectedVersion = null);

    /// <summary>
    /// Outcome of a patch attempt. IsConcurrencyFailure indicates the caller should surface a 409.
    /// </summary>
    public record PatchUserResult(bool Succeeded, bool IsConcurrencyFailure = false, string? Reason = null);

    /// <summary>
    /// Apply users request for a domain-specific bulk action.
    /// </summary>
    public record ApplyUsersRequest(IReadOnlyList<long> UserIds);

    /// <summary>
    /// Apply result contains a simple success flag plus an opaque Summary object for controller-level translation.
    /// </summary>
    public record ApplyUsersResult(bool Succeeded, object? Summary = null, string? Reason = null);

    #endregion
}
