// Repositories/Interfaces/IProjectStateChangeRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    /// <summary>
    /// Persistence boundary for ProjectStateChange records.
    /// Implementations may provide an atomic AppendAndTransitionAsync that
    /// both inserts a state-change record and transitions the project in one DB transaction.
    /// </summary>
    public interface IProjectStateChangeRepository
    {
        /// <summary>
        /// Append a ProjectStateChange record (non-atomic).
        /// </summary>
        Task CreateAsync(ProjectStateChangeEntity change, CancellationToken ct = default);

        /// <summary>
        /// Optional: atomically append a change and transition the project, enforcing optimistic concurrency via expectedVersion.
        /// Returns the transitioned ProjectEntity. If not supported, throw NotSupportedException.
        /// </summary>
        Task<ProjectEntity> AppendAndTransitionAsync(ProjectStateChangeEntity change, int expectedVersion, CancellationToken ct = default);

        /// <summary>
        /// List state changes for a project.
        /// </summary>
        Task<IReadOnlyList<ProjectStateChangeEntity>> ListByProjectIdAsync(long projectId, CancellationToken ct = default);
    }
}
