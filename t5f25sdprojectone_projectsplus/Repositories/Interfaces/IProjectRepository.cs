// Repositories/IProjectRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Repository contract for Project persistence and queries.
    /// Implementations are responsible for:
    /// - maintaining UpdatedAt and Version;
    /// - enforcing workspace-scoped slug uniqueness on Create/Update (map violations to DomainConflictException);
    /// - optimistic concurrency on Update/Delete (map mismatches to DomainConcurrencyException);
    /// - soft-delete semantics are acceptable but must be documented for the implementation.
    /// </summary>
    public interface IProjectRepository
    {
        /// <summary>
        /// Get a project by its numeric id. Returns null when not found or soft-deleted.
        /// </summary>
        Task<ProjectEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find a project by workspace id and slug (case-insensitive). Returns null when not found.
        /// </summary>
        Task<ProjectEntity?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default);

        /// <summary>
        /// List all projects within a workspace. Implementations may return only non-deleted items.
        /// </summary>
        Task<IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);

        /// <summary>
        /// Create a new project. Caller is responsible for setting WorkspaceId.
        /// Implementations must enforce workspace-scoped slug uniqueness and return the persisted project
        /// with Id, timestamps and Version set.
        /// </summary>
        Task<ProjectEntity> CreateAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Update an existing project using optimistic concurrency; on version mismatch throw DomainConcurrencyException.
        /// Returns the updated project.
        /// </summary>
        Task<ProjectEntity> UpdateAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Delete a project using optimistic concurrency. Provide expectedVersion to enforce concurrency;
        /// pass -1 if the implementation does not use version checks.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);
    }
}
