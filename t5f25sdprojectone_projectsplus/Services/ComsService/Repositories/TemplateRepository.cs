// src/ProjectsPlus.Comms/Persistence/TemplateRepository.Ef.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public interface ITemplateRepository
    {
        Task<TemplateDto?> GetByIdAsync(string templateId, CancellationToken ct = default);
        Task<IReadOnlyList<TemplateDto>> ListAsync(string? scope = null, string? scopeKey = null, CancellationToken ct = default);
        Task<TemplateDto> CreateAsync(CreateTemplateRequest req, CancellationToken ct = default);
        Task UpdateAsync(string templateId, Action<TemplateDto> applyChanges, CancellationToken ct = default);
        Task DeleteAsync(string templateId, CancellationToken ct = default);
    }

    public class TemplateRepository : ITemplateRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public TemplateRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<TemplateDto?> GetByIdAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return null;
            var e = await _db.Templates!.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
            return e == null ? null : MapToDto(e);
        }

        public async Task<IReadOnlyList<TemplateDto>> ListAsync(string? scope = null, string? scopeKey = null, CancellationToken ct = default)
        {
            var q = _db.Templates!.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(scope))
                q = q.Where(t => t.Scope == scope);

            if (!string.IsNullOrWhiteSpace(scopeKey))
                q = q.Where(t => t.ScopeKey == scopeKey);

            var items = await q.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
            return items.Select(MapToDto).ToList();
        }

        public async Task<TemplateDto> CreateAsync(CreateTemplateRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var id = string.IsNullOrWhiteSpace(req.TemplateId) ? Guid.NewGuid().ToString("D") : req.TemplateId;

            var entity = new TemplateEntity
            {
                TemplateId = id,
                Name = req.Name ?? string.Empty,
                Scope = req.Scope ?? "Global",
                ScopeKey = req.ScopeKey,
                Format = req.Format ?? "Html",
                SubjectTemplate = req.SubjectTemplate ?? string.Empty,
                BodyTemplate = req.BodyTemplate ?? string.Empty,
                Version = req.Version <= 0 ? 1 : req.Version,
                IsActive = req.IsActive,
                CreatedAt = req.CreatedAt == default ? DateTime.UtcNow : req.CreatedAt,
                UpdatedAt = req.UpdatedAt
            };

            await _db.Templates!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);

            return MapToDto(entity);
        }

        public async Task UpdateAsync(string templateId, Action<TemplateDto> applyChanges, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentNullException(nameof(templateId));
            if (applyChanges == null) throw new ArgumentNullException(nameof(applyChanges));

            var e = await _db.Templates!.FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
            if (e == null) throw new InvalidOperationException("Template not found");

            var dto = MapToDto(e);
            applyChanges(dto);

            // apply back to entity
            e.Name = dto.Name;
            e.Scope = dto.Scope;
            e.ScopeKey = dto.ScopeKey;
            e.Format = dto.Format;
            e.SubjectTemplate = dto.SubjectTemplate;
            e.BodyTemplate = dto.BodyTemplate;
            e.Version = dto.Version;
            e.IsActive = dto.IsActive;
            e.UpdatedAt = DateTime.UtcNow;

            _db.Templates.Update(e);
            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return;

            var e = await _db.Templates!.FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
            if (e == null) return;

            _db.Templates.Remove(e);
            await _db.SaveChangesAsync(ct);
        }

        #region Mapping helpers

        private static TemplateDto MapToDto(TemplateEntity e)
        {
            return new TemplateDto
            {
                TemplateId = e.TemplateId,
                Name = e.Name,
                Scope = e.Scope,
                ScopeKey = e.ScopeKey,
                Format = e.Format,
                SubjectTemplate = e.SubjectTemplate,
                BodyTemplate = e.BodyTemplate,
                Version = e.Version,
                IsActive = e.IsActive,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            };
        }

        #endregion
    }

    #region DTO / Request placeholders

    public sealed class TemplateDto
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Scope { get; set; } = "Global";
        public string? ScopeKey { get; set; }
        public string Format { get; set; } = "Html";
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class CreateTemplateRequest
    {
        public string? TemplateId { get; set; }
        public string? Name { get; set; }
        public string? Scope { get; set; }
        public string? ScopeKey { get; set; }
        public string? Format { get; set; }
        public string? SubjectTemplate { get; set; }
        public string? BodyTemplate { get; set; }
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    #endregion
}
