//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
//using Microsoft.EntityFrameworkCore;
//using t5f25sdprojectone_projectsplus.Data;
//using t5f25sdprojectone_projectsplus.Models;
//using t5f25sdprojectone_projectsplus.Models.Workspaces;

//namespace t5f25sdprojectone_projectsplus.Repositories
//{
//    public class WorkspaceRepository : Interfaces.IWorkspaceRepository
//    {
//        private readonly ProjectsPlusDbContext _db;

//        public WorkspaceRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

//        public async Task<WorkspaceEntity> InsertAsync(WorkspaceEntity workspace, CancellationToken ct = default)
//        {
//            if (workspace == null) throw new ArgumentNullException(nameof(workspace));

//            workspace.Name = workspace.Name?.Trim();
//            workspace.Slug = workspace.Slug?.Trim();
//            workspace.CreatedAt = DateTimeOffset.UtcNow;
//            workspace.UpdatedAt = workspace.CreatedAt;
//            workspace.Version = 1;
//            workspace.IsDeleted = false;

//            _db.Workspaces.Add(workspace);
//            try
//            {
//                await _db.SaveChangesAsync(ct);
//                return workspace;
//            }
//            catch (DbUpdateException ex)
//            {
//                throw MapDbUpdateExceptionToDomainConflict(ex, $"Workspace insert conflict for project {workspace.ProjectId} and slug '{workspace.Slug}'.");
//            }
//        }

//        public async Task<WorkspaceEntity> UpdateAsync(WorkspaceEntity workspace, CancellationToken ct = default)
//        {
//            if (workspace == null) throw new ArgumentNullException(nameof(workspace));

//            var now = DateTimeOffset.UtcNow;

//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE Workspaces
//            SET Name = {workspace.Name},
//                Slug = {workspace.Slug},
//                MetadataJson = {workspace.MetadataJson},
//                OwnerUserId = {workspace.OwnerUserId},
//                UpdatedAt = {now},
//                Version = Version + 1
//            WHERE Id = {workspace.Id} AND Version = {workspace.Version} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Update failed due to version mismatch for workspace {workspace.Id}.");

//            var updated = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspace.Id, ct);
//            return updated;
//        }

//        public async Task<WorkspaceEntity> FindByIdAsync(long id, CancellationToken ct = default)
//        {
//            return await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == id && !w.IsDeleted, ct);
//        }

//        public async Task<WorkspaceEntity> FindByProjectAndSlugAsync(long projectId, string slug, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(slug)) return null;
//            var s = slug.Trim();
//            return await _db.Workspaces.FirstOrDefaultAsync(w => w.ProjectId == projectId && w.Slug == s && !w.IsDeleted, ct);
//        }

//        public async Task<IReadOnlyList<WorkspaceEntity>> ListByProjectAsync(long projectId, CancellationToken ct = default)
//        {
//            var list = await _db.Workspaces
//                .AsNoTracking()
//                .Where(w => w.ProjectId == projectId && !w.IsDeleted)
//                .OrderByDescending(w => w.UpdatedAt)
//                .ToListAsync(ct);

//            return list;
//        }

//        public async Task<WorkspaceEntity> TransitionStateAsync(long workspaceId, WorkspaceState targetState, string metadataJson, int expectedVersion, CancellationToken ct = default)
//        {
//            var now = DateTimeOffset.UtcNow;

//            // Atomically update state and optionally metadata, enforcing optimistic concurrency
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE Workspaces
//            SET State = {(int)targetState},
//                MetadataJson = {metadataJson},
//                UpdatedAt = {now},
//                Version = Version + 1
//            WHERE Id = {workspaceId} AND Version = {expectedVersion} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"State transition failed due to version mismatch for workspace {workspaceId}.");

//            var updated = await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
//            return updated;
//        }

//        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
//        {
//            var now = DateTimeOffset.UtcNow;
//            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
//            UPDATE Workspaces
//            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
//            WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
//            ", ct);

//            if (rows == 0)
//                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing workspace {id}.");
//        }

//        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
//        {
//            var inner = ex.InnerException?.Message ?? ex.Message;
//            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
//            {
//                return new DomainConflictException(fallbackMessage);
//            }
//            return ex;
//        }
//    }
//}
