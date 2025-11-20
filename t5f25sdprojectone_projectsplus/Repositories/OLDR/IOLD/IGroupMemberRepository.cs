// Repositories/IGroupMemberRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace.Group;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    /// <summary>
    /// Repository contract for GroupMember persistence and queries.
    /// - Implementations must manage UpdatedAt/Version.
    /// - Create must enforce uniqueness (group_id, user_id); Update optimistic.
    /// - Soft remove should set RemovedAt; physical delete optional.
    /// </summary>
    public interface IGroupMemberRepository
    {
        Task<GroupMember?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<GroupMember?> FindByGroupAndUserAsync(long groupId, long userId, CancellationToken ct = default);
        Task<IReadOnlyList<GroupMember>> ListByGroupAsync(long groupId, CancellationToken ct = default);
        Task<GroupMember> CreateAsync(GroupMember member, CancellationToken ct = default);
        Task<GroupMember> UpdateAsync(GroupMember member, CancellationToken ct = default);
        Task SoftRemoveAsync(long id, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
