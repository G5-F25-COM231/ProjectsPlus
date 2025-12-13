// src/ProjectsPlus.Comms/Services/TemplateService.InMemory.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    /// <summary>
    /// In-memory TemplateService implementing ITemplateService from the shared Contracts.
    /// Thread-safe, suitable for local development and tests.
    /// Uses simple token replacement for rendering: {{key}} -> value.ToString().
    /// </summary>
   

    public class TemplateServiceInMemory : ITemplateService
    {
        private readonly ConcurrentDictionary<string, TemplateDto> _store = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex TokenRegex = new(@"\{\{\s*(?<key>[^\}\s]+)\s*\}\}", RegexOptions.Compiled);

        public Task<TemplateDto> CreateTemplateAsync(TemplateDto template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            if (string.IsNullOrWhiteSpace(template.TemplateId))
                template.TemplateId = Guid.NewGuid().ToString("D");

            template.CreatedAt = template.CreatedAt == default ? DateTime.UtcNow : template.CreatedAt;
            template.UpdatedAt ??= template.CreatedAt;

            _store[template.TemplateId] = Clone(template);
            return Task.FromResult(Clone(template));
        }

        public Task<TemplateDto?> GetTemplateAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return Task.FromResult<TemplateDto?>(null);
            _store.TryGetValue(templateId, out var t);
            return Task.FromResult(t == null ? null : Clone(t));
        }

        public Task<PagedResult<TemplateDto>> ListTemplatesAsync(TemplateScope? scope = null, string? scopeKey = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        {
            if (pageSize <= 0) pageSize = 50;

            var q = _store.Values.AsEnumerable();

            if (scope.HasValue)
                q = q.Where(t => t.Scope == scope.Value);

            if (!string.IsNullOrWhiteSpace(scopeKey))
                q = q.Where(t => string.Equals(t.ScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase));

            // simple ordering by CreatedAt desc
            var ordered = q.OrderByDescending(t => t.CreatedAt).ToList();

            // continuationToken is treated as the TemplateId of the last item seen
            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                var idx = ordered.FindIndex(x => string.Equals(x.TemplateId, continuationToken, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0 && idx + 1 < ordered.Count)
                    ordered = ordered.Skip(idx + 1).ToList();
                else if (idx >= 0)
                    ordered = new List<TemplateDto>();
            }

            var page = ordered.Take(pageSize).Select(Clone).ToList();
            string? next = null;
            if (page.Count == pageSize)
            {
                next = page.Last().TemplateId;
            }

            var result = new PagedResult<TemplateDto>
            {
                Items = page,
                ContinuationToken = next,
                TotalCount = page.Count
            };

            return Task.FromResult(result);
        }

        public Task<TemplateDto> UpdateTemplateAsync(TemplateDto template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.TemplateId)) throw new ArgumentNullException(nameof(template.TemplateId));

            if (!_store.TryGetValue(template.TemplateId, out var existing))
                throw new InvalidOperationException("Template not found");

            // Merge fields (caller provides full template typically)
            existing.Name = template.Name;
            existing.Scope = template.Scope;
            existing.ScopeKey = template.ScopeKey;
            existing.Format = template.Format;
            existing.SubjectTemplate = template.SubjectTemplate;
            existing.BodyTemplate = template.BodyTemplate;
            existing.Version = template.Version <= 0 ? existing.Version + 1 : template.Version;
            existing.IsActive = template.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            _store[existing.TemplateId] = Clone(existing);
            return Task.FromResult(Clone(existing));
        }

        public Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return Task.FromResult(false);
            var removed = _store.TryRemove(templateId, out _);
            return Task.FromResult(removed);
        }

        /// <summary>
        /// Renders the template using a simple token replacement strategy.
        /// Tokens are of the form {{key}} and will be replaced by variables[key].ToString().
        /// Missing keys are replaced with empty string.
        /// Returns (subject, body).
        /// </summary>
        public Task<(string Subject, string Body)> RenderAsync(TemplateDto template, IDictionary<string, object?>? variables, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));

            var vars = variables ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            string RenderText(string input)
            {
                if (string.IsNullOrEmpty(input)) return string.Empty;

                return TokenRegex.Replace(input, m =>
                {
                    var key = m.Groups["key"].Value;
                    if (string.IsNullOrEmpty(key)) return string.Empty;

                    if (vars.TryGetValue(key, out var val))
                    {
                        return val?.ToString() ?? string.Empty;
                    }

                    // support dotted keys (e.g., user.name)
                    if (key.Contains('.'))
                    {
                        var parts = key.Split('.');
                        object? cur = vars;
                        foreach (var p in parts)
                        {
                            if (cur is IDictionary<string, object?> dict && dict.TryGetValue(p, out var next))
                            {
                                cur = next;
                            }
                            else
                            {
                                cur = null;
                                break;
                            }
                        }
                        return cur?.ToString() ?? string.Empty;
                    }

                    return string.Empty;
                });
            }

            var subject = RenderText(template.SubjectTemplate ?? string.Empty);
            var body = RenderText(template.BodyTemplate ?? string.Empty);

            return Task.FromResult((subject, body));
        }

        #region Helpers

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
