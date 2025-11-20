// Repositories/IKanbanBoardRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    public interface IKanbanBoardRepository
    {
        Task<KanbanBoard?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<KanbanBoard?> FindByScopeAndSlugAsync(long workspaceId, long? projectId, string slug, CancellationToken ct = default);
        Task<IReadOnlyList<KanbanBoard>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);
        Task<IReadOnlyList<KanbanBoard>> ListByProjectAsync(long projectId, CancellationToken ct = default);
        Task<KanbanBoard> CreateAsync(KanbanBoard board, CancellationToken ct = default);
        Task<KanbanBoard> UpdateAsync(KanbanBoard board, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
