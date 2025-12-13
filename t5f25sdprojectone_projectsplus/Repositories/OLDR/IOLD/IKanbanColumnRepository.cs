// Repositories/IKanbanColumnRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    public interface IKanbanColumnRepository
    {
        Task<KanbanColumn?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<IReadOnlyList<KanbanColumn>> ListByBoardAsync(long boardId, CancellationToken ct = default);
        Task<KanbanColumn?> FindByBoardAndSlugAsync(long boardId, string slug, CancellationToken ct = default);
        Task<KanbanColumn> CreateAsync(KanbanColumn column, CancellationToken ct = default);
        Task<KanbanColumn> UpdateAsync(KanbanColumn column, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
        Task ReorderAsync(long boardId, IReadOnlyList<long> orderedColumnIds, CancellationToken ct = default);
    }
}
