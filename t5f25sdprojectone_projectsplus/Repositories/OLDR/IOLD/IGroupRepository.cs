// Repositories/IGroupRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Workspace.Group;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    /// <summary>
    /// Repository contract for Group persistence and queries.
    /// - Implementations manage UpdatedAt/Version.
    /// - Create must enforce workspace-scoped uniqueness; Update must be optimistic.
    /// </summary>
    public interface IGroupRepository
    {
        Task<Group?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<Group?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default);
        Task<IReadOnlyList<Group>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);
        Task<Group> CreateAsync(Group group, CancellationToken ct = default);
        Task<Group> UpdateAsync(Group group, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
