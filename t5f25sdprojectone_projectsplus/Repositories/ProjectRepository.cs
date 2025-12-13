using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class ProjectRepository : IProjectRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public ProjectRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        // Canonical create method used by new callers
        public async Task<ProjectEntity> CreateAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            project.Title = project.Title?.Trim();
            project.Slug = (project.Slug ?? GenerateSlug(project.Title ?? $"project-{project.Id}")).Trim();
            project.CreatedAt = DateTimeOffset.UtcNow;
            project.UpdatedAt = project.CreatedAt;
            project.Version = 1;
            project.IsDeleted = false;

            _db.Projects.Add(project);
            try
            {
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                return project;
            }
            catch (DbUpdateException ex)
            {
                throw MapDbUpdateExceptionToDomainConflict(ex, "Project insert failed due to conflict.");
            }
        }

        // Canonical update
        public async Task<ProjectEntity> UpdateAsync(ProjectEntity project, CancellationToken ct = default)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));

            var now = DateTimeOffset.UtcNow;

            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Projects
                SET Title = {project.Title},
                    ShortDescription = {project.ShortDescription},
                    LongDescription = {project.LongDescription},
                    AdditionCompatibilityJson = {project.AdditionCompatibilityJson},
                    RequiresBaseConsent = {project.RequiresBaseConsent},
                    WorkspaceId = {project.WorkspaceId},
                    Status = {(int)project.Status},
                    Slug = {project.Slug},
                    UpdatedAt = {now},
                    Version = Version + 1
                WHERE Id = {project.Id} AND Version = {project.Version} AND IsDeleted = 0
            ", ct).ConfigureAwait(false);

            if (rows == 0)
                throw new DomainConcurrencyException($"Update failed due to version mismatch for project {project.Id}.");

            var updated = await _db.Projects.FirstOrDefaultAsync(p => p.Id == project.Id, ct).ConfigureAwait(false);
            return updated!;
        }

        // Canonical read by id
        public async Task<ProjectEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
                .ConfigureAwait(false);
        }

        // Canonical find by workspace+slug
        public async Task<ProjectEntity?> FindByWorkspaceAndSlugAsync(long workspaceId, string slug, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(slug)) return null;
            var normalized = slug.Trim();
            return await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.Slug == normalized && !p.IsDeleted, ct)
                .ConfigureAwait(false);
        }

        // Canonical list by workspace
        public async Task<IReadOnlyList<ProjectEntity>> ListByWorkspaceAsync(long workspaceId, CancellationToken ct = default)
        {
            var list = await _db.Projects
                .AsNoTracking()
                .Where(p => p.WorkspaceId == workspaceId && !p.IsDeleted)
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return list;
        }

        // Canonical delete (soft) with optimistic concurrency
        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE Projects
                SET IsDeleted = 1, UpdatedAt = {now}, Version = Version + 1
                WHERE Id = {id} AND Version = {expectedVersion} AND IsDeleted = 0
            ", ct).ConfigureAwait(false);

            if (rows == 0)
                throw new DomainConcurrencyException($"Delete failed due to version mismatch or missing project {id}.");
        }

        // -----------------------
        // Backwards-compatible wrappers (preserve existing test/caller surface)
        // -----------------------

        // Old callers expect InsertAsync — forward to CreateAsync
        public Task<ProjectEntity> InsertAsync(ProjectEntity project, CancellationToken ct = default)
            => CreateAsync(project, ct);

        // Old callers expect FindByIdAsync — forward to GetByIdAsync
        public Task<ProjectEntity?> FindByIdAsync(long id, CancellationToken ct = default)
            => GetByIdAsync(id, ct);

        // Support owner-based listing used by tests: simple non-deleted filter ordered by UpdatedAt desc.
        // Accepts optional filters via the filters parameter (example: (skip,take))
        public async Task<IReadOnlyList<ProjectEntity>> ListByOwnerAsync(long ownerUserId, object filters = null, CancellationToken ct = default)
        {
            var query = _db.Projects
                .AsNoTracking()
                .Where(p => p.OwnerUserId == ownerUserId && !p.IsDeleted)
                .OrderByDescending(p => p.UpdatedAt)
                .AsQueryable();

            if (filters is ValueTuple<int, int> paging)
            {
                var (skip, take) = paging;
                query = query.Skip(skip).Take(take);
            }

            var list = await query.ToListAsync(ct).ConfigureAwait(false);
            return list;
        }

        // -----------------------
        // Helpers
        // -----------------------

        private static string GenerateSlug(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return Guid.NewGuid().ToString("N").Substring(0, 8);
            var s = title.Trim().ToLowerInvariant();
            var chars = s.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '-').ToArray();
            var cleaned = new string(chars);
            var collapsed = Regex.Replace(cleaned, @"\s+", "-");
            return collapsed;
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
