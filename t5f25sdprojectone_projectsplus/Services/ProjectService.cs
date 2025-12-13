// Services/ProjectService.cs
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;
using t5f25sdprojectone_projectsplus.Repositories;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// Pragmatic implementation of IProjectService.
    /// - Validates and normalizes inputs
    /// - Delegates persistence to IProjectRepository
    /// - Appends a ProjectStateChange on submit (persisted via optional state-change repository)
    /// - Assembles ProjectView by joining project + resource records via optional repositories
    /// </summary>
    public class ProjectService : IProjectService
    {
        private readonly IProjectRepository _projects;
        private readonly IProjectStateChangeRepository? _stateChanges;
        private readonly IResourceRecordRepository? _resourceRecords;

        public ProjectService(IProjectRepository projects, IServiceProvider services)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _stateChanges = services.GetService(typeof(IProjectStateChangeRepository)) as IProjectStateChangeRepository;
            _resourceRecords = services.GetService(typeof(IResourceRecordRepository)) as IResourceRecordRepository;
        }

        public async Task<ProjectEntity> CreateProjectAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (string.IsNullOrWhiteSpace(project.Title)) throw new ArgumentException("Title is required.", nameof(project.Title));

            project.Title = project.Title.Trim();
            project.Slug = (project.Slug ?? GenerateSlug(project.Title)).Trim();

            var created = await _projects.CreateAsync(project, ct).ConfigureAwait(false);
            return created;
        }

        public async Task<ProjectEntity> UpdateProjectAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (project.Id <= 0) throw new ArgumentException("Project Id required for update.", nameof(project.Id));

            project.Title = project.Title?.Trim();

            var updated = await _projects.UpdateAsync(project, ct).ConfigureAwait(false);
            return updated;
        }

        public async Task<ProjectEntity> SubmitProjectAsync(long projectId, int expectedVersion, string submitterUserId, string? comment, CancellationToken ct = default)
        {
            if (projectId <= 0) throw new ArgumentException("Invalid projectId.", nameof(projectId));
            if (string.IsNullOrWhiteSpace(submitterUserId)) throw new ArgumentException("submitterUserId is required.", nameof(submitterUserId));

            var project = await _projects.GetByIdAsync(projectId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Project {projectId} not found.");

            if (project.Version != expectedVersion)
                throw new DomainConcurrencyException($"Expected version {expectedVersion} but project has {project.Version}.");

            var change = new ProjectStateChangeEntity
            {
                ProjectId = projectId,
                CreatedAt = DateTimeOffset.UtcNow,
                ActorUserId = long.Parse(submitterUserId),
                FromState = project.Status.ToString(),
                ToState = ProjectStatus.Submitted.ToString(),
                Comment = comment
            };

            // Prefer atomic append-and-transition if repository supports it
            if (_stateChanges != null)
            {
                // AppendAndTransitionAsync should atomically append change and return the transitioned ProjectEntity,
                // enforcing expectedVersion and incrementing Version. If not implemented, repository can throw NotSupportedException.
                try
                {
                    var transitioned = await _stateChanges.AppendAndTransitionAsync(change, expectedVersion, ct).ConfigureAwait(false);
                    return transitioned;
                }
                catch (NotSupportedException)
                {
                    // fallback to manual append + update
                }
            }

            // Fallback path: create state-change record (if repo exists), then update project status via optimistic UpdateAsync
            if (_stateChanges != null)
            {
                await _stateChanges.CreateAsync(change, ct).ConfigureAwait(false);
            }

            var toUpdate = new ProjectEntity
            {
                Id = project.Id,
                Version = project.Version,
                Status = ProjectStatus.Submitted
            };

            var updatedProject = await _projects.UpdateAsync(toUpdate, ct).ConfigureAwait(false);
            return updatedProject;
        }

        public async Task<ProjectView?> GetProjectViewAsync(long projectId, CancellationToken ct = default)
        {
            var project = await _projects.GetByIdAsync(projectId, ct).ConfigureAwait(false);
            if (project == null) return null;

            IReadOnlyList<ResourceRecordEntity> resources = Array.Empty<ResourceRecordEntity>();
            if (_resourceRecords != null)
            {
                resources = await _resourceRecords.ListByProjectIdAsync(projectId, ct).ConfigureAwait(false);
            }

            return new ProjectView
            {
                Project = project,
                ResourceRecords = resources
            };
        }

        public async Task<IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default)
        {
            return await _projects.ListByWorkspaceAsync(workspaceId, ct).ConfigureAwait(false);
        }

        private static string GenerateSlug(string title)
        {
            var s = (title ?? string.Empty).Trim().ToLowerInvariant();
            var chars = System.Linq.Enumerable.Where(s, c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray();
            var cleaned = new string(chars);
            var collapsed = Regex.Replace(cleaned, @"\s+", "-");
            return collapsed;
        }
    }

    /// <summary>
    /// Lightweight view aggregate returned from GetProjectViewAsync.
    /// </summary>
    public sealed class ProjectView
    {
        public ProjectEntity Project { get; set; } = default!;
        public IReadOnlyList<ResourceRecordEntity> ResourceRecords { get; set; } = Array.Empty<ResourceRecordEntity>();
    }
}
