// Repositories/IProjectRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Project;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Repository contract for Project persistence and queries.
    /// - Implementations manage UpdatedAt/Version.
    /// - Create must set WorkspaceId and enforce workspace-scoped slug uniqueness.
    /// - Update must perform optimistic concurrency.
    /// </summary>
    public interface IProjectRepository
    {
        Task<Project?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<Project?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default);
        Task<IReadOnlyList<Project>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default);
        Task<Project> CreateAsync(Project project, CancellationToken ct = default);
        Task<Project> UpdateAsync(Project project, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
