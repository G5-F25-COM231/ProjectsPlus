// Repositories/IWorkspaceMemberRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Repository contract for WorkspaceMember persistence and queries.
    /// - Implementations must manage UpdatedAt/Version.
    /// - Create should validate uniqueness (workspace_id, user_id) and return created entity.
    /// - Update should perform optimistic concurrency.
    /// - Soft removal should set RemovedAt; physical deletes optional.
    /// </summary>
    public interface IWorkspaceMemberRepository
    {
        Task<WorkspaceMember?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<WorkspaceMember?> FindByWorkspaceAndUserAsync(long workspaceId, long userId, CancellationToken ct = default);
        Task<IReadOnlyList<WorkspaceMember>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);
        Task<WorkspaceMember> CreateAsync(WorkspaceMember member, CancellationToken ct = default);
        Task<WorkspaceMember> UpdateAsync(WorkspaceMember member, CancellationToken ct = default);
        Task SoftRemoveAsync(long id, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
