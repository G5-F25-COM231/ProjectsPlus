// Services/Interfaces/IProjectService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Application service boundary for project operations.
    /// Responsibilities: input validation/normalization, orchestration of repository calls,
    /// creation of ProjectStateChange records on submit, and mapping repository exceptions to service-level semantics.
    /// </summary>
    public interface IProjectService
    {
        /// <summary>
        /// Create a new project. Returns the persisted project with Id, timestamps and Version populated.
        /// </summary>
        Task<ProjectEntity> CreateProjectAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Update an existing project. Uses optimistic concurrency via the repository's Version semantics.
        /// Returns the updated project.
        /// </summary>
        Task<ProjectEntity> UpdateProjectAsync(ProjectEntity project, CancellationToken ct = default);

        /// <summary>
        /// Submit a project. Appends a ProjectStateChange to represent the submit action and advances project state.
        /// Returns the updated project after the submit.
        /// </summary>
        Task<ProjectEntity> SubmitProjectAsync(long projectId, int expectedVersion, string submitterUserId, string? comment, CancellationToken ct = default);

        /// <summary>
        /// Returns a project view that includes the project entity and any related resource records needed by the UI.
        /// If not found, returns null.
        /// </summary>
        Task<ProjectView?> GetProjectViewAsync(long projectId, CancellationToken ct = default);

        /// <summary>
        /// List projects by workspace for UI/listing endpoints.
        /// </summary>
        Task<IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);
    }
}
