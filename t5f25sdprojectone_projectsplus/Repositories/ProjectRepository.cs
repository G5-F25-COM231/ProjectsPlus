using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories;

namespace t5f25sdprojectone_projectsplus.Repositories.EntityFramework
{
    public class ProjectRepository : Repositories.Interfaces.IProjectRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public ProjectRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<ProjectEntity> InsertAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            project.Title = project.Title?.Trim();
            project.CreatedAt = DateTimeOffset.UtcNow;
            project.UpdatedAt = project.CreatedAt;
            project.Version = 1;
            project.IsDeleted = false;

            _db.Projects.Add(project);
            try
            {
                await _db.SaveChangesAsync(ct);
                return project;
            }
            catch (DbUpdateException ex)
            {
                // Map unique/index violations to DomainConflictException where appropriate
                throw MapDbUpdateExceptionToDomainConflict(ex, "Project insert failed due to conflict.");
            }
        }

        public async Task<ProjectEntity> UpdateAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var now = DateTimeOffset.UtcNow;

            // Use raw SQL update to enforce optimistic concurrency (Version check) atomically
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE Projects
            SET Title = {project.Title},
                ShortDescription = {project.ShortDescription},
                LongDescription = {project.LongDescription},
                AdditionCompatibilityJson = {project.AdditionCompatibilityJson},
                RequiresBaseConsent = {project.RequiresBaseConsent},
                WorkspaceId = {project.WorkspaceId},
                Status = {(int)project.Status},
                UpdatedAt = {now},
                Version = Version + 1
            WHERE Id = {project.Id} AND Version = {project.Version} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Update failed due to version mismatch for project {project.Id}.");

            var updated = await _db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct);
            return updated;
        }

        public async Task<ProjectEntity> FindByIdAsync(long projectId, CancellationToken ct = default)
        {
            return await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, ct);
        }

        public async Task<IReadOnlyList<ProjectEntity>> ListByOwnerAsync(long ownerUserId, object filters = null, CancellationToken ct = default)
        {
            // Basic implementation: list non-deleted projects owned by ownerUserId ordered by UpdatedAt desc.
            // Caller may pass filters later (paging, status, search).
            var query = _db.Projects
                .AsNoTracking()
                .Where(p => p.OwnerUserId == ownerUserId && !p.IsDeleted)
                .OrderByDescending(p => p.UpdatedAt)
                .AsQueryable();

            // Optional simple filter object support: if filters is a tuple (int skip, int take) apply paging.
            if (filters is ValueTuple<int, int> paging)
            {
                var (skip, take) = paging;
                query = query.Skip(skip).Take(take);
            }

            var list = await query.ToListAsync(ct);
            return list;
        }

        public async Task DeleteAsync(long projectId, int expectedVersion, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE Projects
            SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
            WHERE Id = {projectId} AND Version = {expectedVersion} AND IsDeleted = 0
            ", ct);

            if (rows == 0)
                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing project {projectId}.");
        }

        private static Exception MapDbUpdateExceptionToDomainConflict(DbUpdateException ex, string fallbackMessage)
        {
            var inner = ex.InnerException?.Message ?? ex.Message;
            if (inner != null && (inner.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || inner.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                return new DomainConflictException(fallbackMessage);
            }
            return ex;
        }
    }
}
