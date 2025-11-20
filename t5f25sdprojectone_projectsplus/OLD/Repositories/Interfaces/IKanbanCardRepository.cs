// Repositories/IKanbanCardRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IKanbanCardRepository
    {
        Task<KanbanCard?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<IReadOnlyList<KanbanCard>> ListByColumnAsync(long columnId, CancellationToken ct = default);
        Task<KanbanCard> CreateAsync(KanbanCard card, CancellationToken ct = default);
        Task<KanbanCard> UpdateAsync(KanbanCard card, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
        Task ReorderWithinColumnAsync(long columnId, IReadOnlyList<long> orderedCardIds, CancellationToken ct = default);
        Task AddAttachmentAsync(KanbanCardAttachment attachment, CancellationToken ct = default);
        Task AddAssigneeAsync(KanbanCardAssignee assignee, CancellationToken ct = default);
        Task RemoveAssigneeAsync(long assigneeId, CancellationToken ct = default);
    }
}
