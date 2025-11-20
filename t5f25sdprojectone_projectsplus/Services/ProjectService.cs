// Services/ProjectService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// A pragmatic, minimal implementation of IProjectService that delegates to IProjectRepository.
    /// It performs basic validation and maps repository concurrency/conflict exceptions through.
    /// Extend with authorization, audit logging, or richer business rules as required.
    /// </summary>
    public class ProjectService : IProjectService
    {
        private readonly IProjectRepository _projects;

        public ProjectService(IProjectRepository projects)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        }

        public async Task<ProjectEntity> CreateAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.Title)) throw new ArgumentException("Title is required.", nameof(project.Title));

            // Normalise title/slug early
            project.Title = project.Title.Trim();
            project.Slug = (project.Slug ?? project.Title).Trim();

            return await _projects.CreateAsync(project, ct).ConfigureAwait(false);
        }

        public async Task<ProjectEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            return await _projects.GetByIdAsync(id, ct).ConfigureAwait(false);
        }

        public async Task<ProjectEntity?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            return await _projects.FindByWorkspaceAndSlugAsync(workspaceId, slug.Trim(), ct).ConfigureAwait(false);
        }

        public async Task<System.Collections.Generic.IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default)
        {
            return await _projects.ListByWorkspaceAsync(workspaceId, ct).ConfigureAwait(false);
        }

        public async Task<ProjectEntity> UpdateAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (project.Id <= 0) throw new ArgumentException("Project Id is required for update.", nameof(project.Id));

            // Basic normalization
            project.Title = project.Title?.Trim();

            return await _projects.UpdateAsync(project, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Invalid project id.", nameof(id));
            await _projects.DeleteAsync(id, expectedVersion, ct).ConfigureAwait(false);
        }

        public async Task<ProjectEntity> ChangeStatusAsync(long projectId, ProjectStatus newStatus, int expectedVersion, CancellationToken ct = default)
        {
            if (projectId <= 0) throw new ArgumentException("Invalid project id.", nameof(projectId));
            // Business rules (example): only allow certain transitions here if needed.
            return await _projects.UpdateAsync(new ProjectEntity
            {
                Id = projectId,
                Status = newStatus,
                Version = expectedVersion
            }, ct).ConfigureAwait(false);
        }
    }
}
