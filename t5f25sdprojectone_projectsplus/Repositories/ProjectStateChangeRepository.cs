// src/Repositories/EF/ProjectStateChangeRepository.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories.EF
{
    public class ProjectStateChangeRepository : IProjectStateChangeRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public ProjectStateChangeRepository(ProjectsPlusDbContext db) => _db = db;

        public async Task CreateAsync(ProjectStateChangeEntity entity, CancellationToken ct = default)
        {
            _db.ProjectStateChanges.Add(entity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<ProjectEntity> AppendAndTransitionAsync(ProjectStateChangeEntity change, int expectedVersion, CancellationToken ct = default)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var project = await _db.Projects
                .Where(p => p.Id == change.ProjectId)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (project == null)
                throw new InvalidOperationException($"Project {change.ProjectId} not found.");

            if (project.Version != expectedVersion)
                throw new DbUpdateConcurrencyException($"Expected project version {expectedVersion}, actual {project.Version}.");

            _db.ProjectStateChanges.Add(change);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(change.ToState))
            {
                if (Enum.TryParse(typeof(ProjectStatus), change.ToState, ignoreCase: true, out var parsed))
                    project.Status = (ProjectStatus)parsed;
            }

            project.Version = project.Version + 1;
            _db.Projects.Update(project);

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await tx.CommitAsync(ct).ConfigureAwait(false);

            return project;
        }

        public async Task<IReadOnlyList<ProjectStateChangeEntity>> ListByProjectIdAsync(long projectId, CancellationToken ct = default)
        {
            var list = await _db.ProjectStateChanges
                .AsNoTracking()
                .Where(c => c.ProjectId == projectId && !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return (IReadOnlyList<ProjectStateChangeEntity>)list;
        }
    }
}
