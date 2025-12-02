// src/ProjectsPlus.Comms/Persistence/InMemory/TemplateRepository.InMemory.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.GithubService;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    // In-memory ITemplateRepository implementation for local development and tests.
    // API mirrors TemplateRepositoryEf: GetByIdAsync, ListAsync, CreateAsync, UpdateAsync, DeleteAsync.
   
    public class TemplateRepositoryInMemory : ITemplateRepository
    {
        private readonly ConcurrentDictionary<string, TemplateDto> _store = new(StringComparer.OrdinalIgnoreCase);

        public Task<TemplateDto?> GetAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return Task.FromResult<TemplateDto?>(null);
            _store.TryGetValue(templateId, out var t);
            return Task.FromResult(t == null ? null : Clone(t));
        }
        public Task<TemplateDto?> GetByIdAsync(string templateId, CancellationToken ct = default)
        {
            return GetAsync(templateId, ct);
        }

        public async Task<PagedResult<TemplateDto>> ListAsync(TemplateScope? scope, string? scopeKey, int pageSize, string? continuationToken, CancellationToken ct = default)
        {
            var items = await ListAsync(scope.ToString(), scopeKey, ct);
      
            return new PagedResult<TemplateDto> { Items = items, TotalCount = items.Count };

        }       

        public Task<IReadOnlyList<TemplateDto>> ListAsync(string? scope = null, string? scopeKey = null, CancellationToken ct = default)
        {
            var q = _store.Values.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(scope))
                q = q.Where(x => string.Equals(x.Scope.ToString(), scope, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(scopeKey))
                q = q.Where(x => string.Equals(x.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase));

            var list = q.OrderByDescending(x => x.CreatedAt).Select(Clone).ToList();
            return Task.FromResult((IReadOnlyList<TemplateDto>)list);
        }

        public Task<TemplateDto> CreateAsync(TemplateDto req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var id = string.IsNullOrWhiteSpace(req.TemplateId) ? Guid.NewGuid().ToString("D") : req.TemplateId;

            var dto = new TemplateDto
            {
                TemplateId = id,
                Name = req.Name ?? string.Empty,
                Scope = req?.Scope ?? TemplateScope.Global,
                ScopeKey = req.ScopeKey,
                Format = req?.Format ?? TemplateFormat.Html,
                SubjectTemplate = req.SubjectTemplate ?? string.Empty,
                BodyTemplate = req.BodyTemplate ?? string.Empty,
                Version = req.Version <= 0 ? 1 : req.Version,
                IsActive = req.IsActive,
                CreatedAt = req.CreatedAt == default ? DateTime.UtcNow : req.CreatedAt,
                UpdatedAt = req.UpdatedAt
            };

            _store[dto.TemplateId] = Clone(dto);
            return Task.FromResult(Clone(dto));
        }

        public Task<TemplateDto> UpdateAsync(TemplateDto template, CancellationToken ct = default)
        {
            //Action<TemplateDto> temp = t => template = t;
            void temp(TemplateDto t) => template = t;
            UpdateAsync(template.TemplateId, temp, ct);

            return Task.FromResult(Clone(template));
        }

        public Task UpdateAsync(string templateId, Action<TemplateDto> applyChanges, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) throw new ArgumentNullException(nameof(templateId));
            if (applyChanges == null) throw new ArgumentNullException(nameof(applyChanges));

            if (!_store.TryGetValue(templateId, out var existing))
                throw new InvalidOperationException("Template not found");

            // Work on a copy to avoid races while applying changes
            var copy = Clone(existing);
            applyChanges(copy);
            copy.UpdatedAt = DateTime.UtcNow;
            // bump version if caller changed content (best-effort)
            copy.Version = Math.Max(1, copy.Version);

            _store[templateId] = Clone(copy);
            return Task.CompletedTask;
        }


        Task<bool> ITemplateRepository.DeleteAsync(string templateId, CancellationToken ct)
        {
            return Task.FromResult<bool>(DeleteAsync(templateId, ct).GetAwaiter().IsCompleted);
        }

        public Task DeleteAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return Task.CompletedTask;
            _store.TryRemove(templateId, out _);
            return Task.CompletedTask;
        }

        #region Helpers (cloning)

        private static TemplateDto Clone(TemplateDto src)
        {
            if (src == null) return null!;

            return new TemplateDto
            {
                TemplateId = src.TemplateId,
                Name = src.Name,
                Scope = src.Scope,
                ScopeKey = src.ScopeKey,
                Format = src.Format,
                SubjectTemplate = src.SubjectTemplate,
                BodyTemplate = src.BodyTemplate,
                Version = src.Version,
                IsActive = src.IsActive,
                CreatedAt = src.CreatedAt,
                UpdatedAt = src.UpdatedAt
            };
        }

   

        #endregion
    }

  
}
