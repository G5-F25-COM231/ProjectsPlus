// Repositories/IWorkspaceRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Minimal repository contract for Workspace operations.
    /// Implementations must manage CreatedAt/UpdatedAt and Version increments,
    /// enforce slug uniqueness where applicable, and map persistence-level errors
    /// to DomainConflictException / DomainConcurrencyException as appropriate.
    /// Service layer should call these methods with normalized slug values.
    /// </summary>
    public interface IWorkspaceRepository
    {
        /// <summary>
        /// Get a workspace by its numeric id. Returns null when not found or soft-deleted.
        /// </summary>
        Task<WorkspaceEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find a workspace by slug (case-insensitive). Returns null when not found.
        /// </summary>
        Task<WorkspaceEntity?> FindBySlugAsync(string slug, CancellationToken ct = default);

        /// <summary>
        /// Create a new workspace. Caller should set OwnerUserId and Slug (normalized).
        /// Returns the persisted workspace with Id, timestamps and Version set.
        /// </summary>
        Task<WorkspaceEntity> CreateAsync(WorkspaceEntity workspace, CancellationToken ct = default);

        /// <summary>
        /// Update an existing workspace using optimistic concurrency; on version mismatch throw DomainConcurrencyException.
        /// Returns the updated workspace.
        /// </summary>
        Task<WorkspaceEntity> UpdateAsync(WorkspaceEntity workspace, CancellationToken ct = default);

        /// <summary>
        /// Soft-delete a workspace. Implementations may accept an expectedVersion overload to enforce concurrency.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// List workspaces owned by a specific user. Implementations may return only non-deleted items.
        /// </summary>
        Task<IReadOnlyList<WorkspaceEntity>> ListByOwnerAsync(long ownerUserId, CancellationToken ct = default);

        /// <summary>
        /// Atomically transition the workspace state and optionally update MetadataJson,
        /// enforcing optimistic concurrency via expectedVersion. Returns the updated workspace.
        /// </summary>
        Task<WorkspaceEntity> TransitionStateAsync(long workspaceId, WorkspaceState targetState, string? metadataJson, int expectedVersion, CancellationToken ct = default);
    }
}
