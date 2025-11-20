// Services/Interfaces/IProjectService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Application-level service boundary for Projects.
    /// Responsibilities: validation, authorization checks, audit logging, mapping domain errors
    /// (conflicts, concurrency) to service-level exceptions, and orchestration of repository calls.
    /// </summary>
    public interface IProjectService
    {
        /// <summary>
        /// Create a new project. Caller must set WorkspaceId when appropriate.
        /// Returns the persisted project with Id, timestamps and Version.
        /// Throws ArgumentException for validation failures and DomainConflictException for uniqueness violations.
        /// </summary>
        Task<ProjectEntity> CreateAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Get a project by numeric id. Returns null if not found or soft-deleted.
        /// </summary>
        Task<ProjectEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Find a project by workspace and slug. Returns null if not found.
        /// </summary>
        Task<ProjectEntity?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default);

        /// <summary>
        /// List projects in a workspace. Implementations may return only non-deleted items.
        /// </summary>
        Task<IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);

        /// <summary>
        /// Update an existing project using optimistic concurrency. On version mismatch throw DomainConcurrencyException.
        /// Returns the updated project.
        /// </summary>
        Task<ProjectEntity> UpdateAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Delete a project using optimistic concurrency. On mismatch throw DomainConcurrencyException.
        /// </summary>
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// Optionally change project status (e.g., publish/unpublish) while enforcing business rules.
        /// Returns the updated project.
        /// </summary>
        Task<ProjectEntity> ChangeStatusAsync(long projectId, ProjectStatus newStatus, int expectedVersion, CancellationToken ct = default);
    }
}
