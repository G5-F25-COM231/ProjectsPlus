// Services/WorkspaceService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Workspaces;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// Application service for workspace operations.
    /// Responsibilities: input validation, normalization, orchestration of repository calls,
    /// and mapping repository exceptions to service boundary semantics.
    /// Extend with authorization and audit logging as needed.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
        private readonly IWorkspaceRepository _workspaces;

        public WorkspaceService(IWorkspaceRepository workspaces)
        {
            _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        }

        public async Task<WorkspaceEntity> CreateAsync(WorkspaceEntity workspace, CancellationToken ct = default)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (workspace.ProjectId <= 0) throw new ArgumentException("ProjectId is required.", nameof(workspace.ProjectId));
            if (string.IsNullOrWhiteSpace(workspace.Name)) throw new ArgumentException("Name is required.", nameof(workspace.Name));
            if (string.IsNullOrWhiteSpace(workspace.Slug)) throw new ArgumentException("Slug is required.", nameof(workspace.Slug));

            workspace.Name = workspace.Name.Trim();
            workspace.Slug = workspace.Slug.Trim().ToLowerInvariant();

            return await _workspaces.CreateAsync(workspace, ct).ConfigureAwait(false);
        }

        public async Task<WorkspaceEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Invalid workspace id.", nameof(id));
            return await _workspaces.GetByIdAsync(id, ct).ConfigureAwait(false);
        }

        public async Task<WorkspaceEntity?> FindBySlugAsync(string slug, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            return await _workspaces.FindBySlugAsync(slug.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<WorkspaceEntity>> ListByOwnerAsync(long ownerUserId, CancellationToken ct = default)
        {
            if (ownerUserId <= 0) throw new ArgumentException("Invalid owner user id.", nameof(ownerUserId));
            return await _workspaces.ListByOwnerAsync(ownerUserId, ct).ConfigureAwait(false);
        }

        public async Task<WorkspaceEntity> UpdateAsync(WorkspaceEntity workspace, CancellationToken ct = default)
        {
            if (workspace == null) throw new ArgumentNullException(nameof(workspace));
            if (workspace.Id <= 0) throw new ArgumentException("Workspace Id is required for update.", nameof(workspace.Id));

            workspace.Name = workspace.Name?.Trim();
            workspace.Slug = workspace.Slug?.Trim().ToLowerInvariant();

            return await _workspaces.UpdateAsync(workspace, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentException("Invalid workspace id.", nameof(id));
            await _workspaces.DeleteAsync(id, expectedVersion, ct).ConfigureAwait(false);
        }

        public async Task<WorkspaceEntity> TransitionStateAsync(long workspaceId, WorkspaceState targetState, string? metadataJson, int expectedVersion, CancellationToken ct = default)
        {
            if (workspaceId <= 0) throw new ArgumentException("Invalid workspace id.", nameof(workspaceId));
            return await _workspaces.TransitionStateAsync(workspaceId, targetState, metadataJson, expectedVersion, ct).ConfigureAwait(false);
        }
    }
}
