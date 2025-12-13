//// Repositories/IWorkspaceRepository.cs
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using t5f25sdprojectone_projectsplus.OLD.Workspace;

//namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
//{
//    /// <summary>
//    /// Minimal repository contract for Workspace operations.
//    /// - Implementations must manage CreatedAt/UpdatedAt and Version increments.
//    /// - Service layer should call these methods with normalized slug values.
//    /// - Update should perform optimistic concurrency checks (compare Version).
//    /// </summary>
//    public interface IWorkspaceRepository
//    {
//        Task<Workspace?> GetByIdAsync(long id, CancellationToken ct = default);
//        Task<Workspace?> FindBySlugAsync(string slug, CancellationToken ct = default);
//        Task<Workspace> CreateAsync(Workspace workspace, CancellationToken ct = default);
//        Task<Workspace> UpdateAsync(Workspace workspace, CancellationToken ct = default);
//        Task DeleteAsync(long id, CancellationToken ct = default);
//        Task<IReadOnlyList<Workspace>> ListByOwnerAsync(long ownerUserId, CancellationToken ct = default);
//    }
//}
