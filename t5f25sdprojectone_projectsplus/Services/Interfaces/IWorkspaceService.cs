// Services/Interfaces/IWorkspaceService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Application service boundary for workspace operations.
    /// Responsibilities: validation, authorization checks, audit logging, orchestration of repository calls,
    /// and mapping repository-level exceptions to service-level exceptions.
    /// </summary>
    public interface IWorkspaceService
    {
        Task<WorkspaceEntity> CreateAsync(WorkspaceEntity workspace, CancellationToken ct = default);
        Task<WorkspaceEntity?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<WorkspaceEntity?> FindBySlugAsync(string slug, CancellationToken ct = default);
        Task<IReadOnlyList<WorkspaceEntity>> ListByOwnerAsync(long ownerUserId, CancellationToken ct = default);
        Task<WorkspaceEntity> UpdateAsync(WorkspaceEntity workspace, CancellationToken ct = default);
        Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default);
        Task<WorkspaceEntity> TransitionStateAsync(long workspaceId, WorkspaceState targetState, string? metadataJson, int expectedVersion, CancellationToken ct = default);
    }
}
