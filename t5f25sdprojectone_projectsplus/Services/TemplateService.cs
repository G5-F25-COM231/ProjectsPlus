// src/ProjectsPlus.Comms/Services/TemplateService.cs
using System.Text.RegularExpressions;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

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
            return await _repo.GetByIdAsync(templateId, ct);
        }
        
        public async Task<PagedResult<TemplateDto>> ListTemplatesAsync(
            TemplateScope? scope = null,
            string? scopeKey = null,
            int pageSize = 50,
            string? continuationToken = null,
            CancellationToken ct = default)
        {
            if (pageSize <= 0) pageSize = 50;

            // repo returns IReadOnlyList<TemplateDto>
            var items = await _repo.ListAsync(scope?.ToString(), scopeKey, pageSize, continuationToken, ct);

            // Map the list into a PagedResult. Adjust property names below to match your PagedResult<T> definition.
            return new PagedResult<TemplateDto>
            {
                Items = items,
                TotalCount = items?.Count ?? 0,                 // optional: only if PagedResult has Total
                ContinuationToken = null                   // set to repo-provided token if available
            };
        }


        public async Task<TemplateDto> UpdateTemplateAsync(TemplateDto template, CancellationToken ct = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.TemplateId)) throw new ArgumentNullException(nameof(template.TemplateId));
            NormalizeTemplate(template);
            var updatedTemp = new Action<TemplateDto>(t => template = t);
            try
            {
                await _repo.UpdateAsync(template.TemplateId, updatedTemp, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return template;
        }

        public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return false;
            try
            {
                await _repo.DeleteAsync(templateId, ct); // void call
                return true;
            }
            catch
            {
                return false;
            }

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
