// src/ProjectsPlus.Comms/Services/TemplateService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// TemplateService
    /// - Thin service layer around ITemplateRepository
    /// - Provides rendering (simple token replacement) and basic validation/normalization
    /// - Suitable for production (backed by EF repository) and tests (backed by in-memory repo)
    /// </summary>
    public class TemplateService : ITemplateService
    {
        private readonly ITemplateRepository _repo;
        private static readonly Regex TokenRegex = new(@"\{\{\s*(?<key>[^\}\s]+)\s*\}\}", RegexOptions.Compiled);

        public TemplateService(ITemplateRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public async Task<TemplateDto> CreateTemplateAsync(TemplateDto template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            NormalizeTemplate(template);

            // repository.CreateAsync expects a TemplateDto per Contracts
            var created = await _repo.CreateAsync(template, ct);
            return created;
        }

        public async Task<TemplateDto?> GetTemplateAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return null;
            return await _repo.GetAsync(templateId, ct);
        }

        public async Task<PagedResult<TemplateDto>> ListTemplatesAsync(TemplateScope? scope = null, string? scopeKey = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        {
            if (pageSize <= 0) pageSize = 50;
            return await _repo.ListAsync(scope, scopeKey, pageSize, continuationToken, ct);
        }

        public async Task<TemplateDto> UpdateTemplateAsync(TemplateDto template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.TemplateId)) throw new ArgumentNullException(nameof(template.TemplateId));
            NormalizeTemplate(template);

            var updated = await _repo.UpdateAsync(template, ct);
            return updated;
        }

        public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return false;
            return await _repo.DeleteAsync(templateId, ct);
        }

        /// <summary>
        /// Render a template using a simple token replacement strategy.
        /// Tokens: {{key}} or dotted keys like {{user.name}}.
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

                    // direct lookup
                    if (vars.TryGetValue(key, out var val))
                        return val?.ToString() ?? string.Empty;

                    // dotted lookup (e.g., user.name)
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

        private static void NormalizeTemplate(TemplateDto t)
        {
            // Ensure required defaults and normalize enum/string fields for repository compatibility
            if (string.IsNullOrWhiteSpace(t.TemplateId))
                t.TemplateId = Guid.NewGuid().ToString("D");

            if (string.IsNullOrWhiteSpace(t.Name))
                t.Name = "Unnamed template";

            // Ensure CreatedAt is set for new templates
            if (t.CreatedAt == default) t.CreatedAt = DateTime.UtcNow;

            // Ensure Format/Scope are set as strings expected by repository implementations
            // (Repository implementations in this codebase accept TemplateDto with enums; if your repo expects strings, adapt there.)
            t.UpdatedAt ??= t.CreatedAt;
        }

        #endregion
    }
}
