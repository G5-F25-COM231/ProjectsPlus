// Repositories/IProjectMemberRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Project;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    /// <summary>
    /// Repository contract for ProjectMember persistence and queries.
    /// - Implementations must manage UpdatedAt/Version.
    /// - Create must enforce uniqueness (project_id, user_id); Update optimistic.
    /// - Soft remove should set RemovedAt; physical delete optional.
    /// </summary>
    public interface IProjectMemberRepository
    {
        Task<ProjectMember?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<ProjectMember?> FindByProjectAndUserAsync(long projectId, long userId, CancellationToken ct = default);
        Task<IReadOnlyList<ProjectMember>> ListByProjectAsync(long projectId, CancellationToken ct = default);
        Task<ProjectMember> CreateAsync(ProjectMember member, CancellationToken ct = default);
        Task<ProjectMember> UpdateAsync(ProjectMember member, CancellationToken ct = default);
        Task SoftRemoveAsync(long id, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
