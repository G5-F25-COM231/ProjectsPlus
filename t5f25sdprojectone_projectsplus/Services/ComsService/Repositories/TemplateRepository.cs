// src/ProjectsPlus.Comms/Persistence/TemplateRepository.Ef.cs
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public interface ITemplateRepository
    {
        Task<TemplateDto?> GetByIdAsync(string templateId, CancellationToken ct = default);
        Task<IReadOnlyList<TemplateDto>> ListAsync(string? scope = null, string? scopeKey = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
        Task<TemplateDto> CreateAsync(TemplateDto req, CancellationToken ct = default);
        Task UpdateAsync(string templateId, Action<TemplateDto> applyChanges, CancellationToken ct = default);
        Task DeleteAsync(string templateId, CancellationToken ct = default);
    }

    public class TemplateRepository : ITemplateRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public TemplateRepository(IServiceProvider svc)
        {
            using var scope = svc.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<TemplateDto?> GetByIdAsync(string templateId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(templateId)) return null;
            var e = await _db.Templates!.AsNoTracking().FirstOrDefaultAsync(t => t.TemplateId == templateId, ct);
            return e == null ? null : MapToDto(e);
        }

        //public async Task<IReadOnlyList<TemplateDto>> ListAsync(string? scope = null, string? scopeKey = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        //{
        //    var q = _db.Templates!.AsNoTracking().AsQueryable();

        //    if (!string.IsNullOrWhiteSpace(scope))
        //        q = q.Where(t => t.Scope == scope);

        //    if (!string.IsNullOrWhiteSpace(scopeKey))
        //        q = q.Where(t => t.ScopeKey == scopeKey);

        //    var items = await q.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        //    return items.Select(MapToDto).ToList();
        //}

        public async Task<IReadOnlyList<TemplateDto>> ListAsync(
        string? scope = null,
        string? scopeKey = null,
        int pageSize = 50,
        string? continuationToken = null,
        CancellationToken ct = default)
        {
            if (pageSize <= 0) pageSize = 50;

            // Base query
            var q = _db.Templates!.AsNoTracking().AsQueryable();

            // Filters
            if (!string.IsNullOrWhiteSpace(scope))
                q = q.Where(t => t.Scope == scope);

            if (!string.IsNullOrWhiteSpace(scopeKey))
                q = q.Where(t => t.ScopeKey == scopeKey);

            // Deterministic ordering: newest first, tie-break by TemplateId
            q = q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.TemplateId);

            // Apply continuation token if present.
            // Token format: base64("{ticks}:{templateId}") where ticks = CreatedAt.Ticks of the last item returned.
            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                if (TryDecodeContinuationToken(continuationToken, out var tokenTicks, out var tokenTemplateId))
                {
                    var tokenCreatedAt = new DateTime(tokenTicks, DateTimeKind.Utc);

                    // For descending order, we want items strictly older than the token position.
                    q = q.Where(t =>
                        t.CreatedAt < tokenCreatedAt
                        || (t.CreatedAt == tokenCreatedAt && string.Compare(t.TemplateId, tokenTemplateId, StringComparison.Ordinal) < 0));
                }
                else
                {
                    // If token is invalid, ignore it (alternatively you could throw).
                }
            }

            // Page
            var items = await q.Take(pageSize).ToListAsync(ct).ConfigureAwait(false);

            return items.Select(MapToDto).ToList();

            // Local helpers
            static bool TryDecodeContinuationToken(string token, out long ticks, out string templateId)
            {
                ticks = 0;
                templateId = string.Empty;
                try
                {
                    var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(token));
                    var parts = decoded.Split(':', 2);
                    if (parts.Length != 2) return false;
                    if (!long.TryParse(parts[0], out ticks)) return false;
                    templateId = parts[1];
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        // Optional helper to create a continuation token for the last item returned:
        // var token = CreateContinuationToken(last.CreatedAt, last.TemplateId);
        private static string CreateContinuationToken(DateTime createdAt, string templateId)
        {
            var payload = $"{createdAt.ToUniversalTime().Ticks}:{templateId}";
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
        }


        public async Task<TemplateDto> CreateAsync(TemplateDto req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var id = string.IsNullOrWhiteSpace(req.TemplateId) ? Guid.NewGuid().ToString("D") : req.TemplateId;

            var entity = new TemplateEntity
            {
                TemplateId = id,
                Name = req.Name ?? string.Empty,
                Scope = req?.Scope.ToString() ?? "Global",
                ScopeKey = req?.ScopeKey,
                Format = req?.Format.ToString() ?? "Html",
                SubjectTemplate = req?.SubjectTemplate ?? string.Empty,
                BodyTemplate = req?.BodyTemplate ?? string.Empty,
                Version = req?.Version <= 0 ? 1 : req.Version,
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
            e.Scope = dto.Scope.ToString();
            e.ScopeKey = dto.ScopeKey;
            e.Format = dto.Format.ToString();
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
        public static TEnum ToEnumOrDefault<TEnum>(string value, TEnum @default = default) where TEnum : struct, Enum => string.IsNullOrWhiteSpace(value) ? @default : (Enum.TryParse<TEnum>(value, true, out var v) && Enum.IsDefined(typeof(TEnum), v)) ? v : (long.TryParse(value, out var n) && Enum.IsDefined(typeof(TEnum), Enum.ToObject(typeof(TEnum), n))) ? (TEnum)Enum.ToObject(typeof(TEnum), n) : @default;

        private static TemplateDto MapToDto(TemplateEntity e)
        {
            return new TemplateDto
            {
                TemplateId = e.TemplateId,
                Name = e.Name,
                Scope = ToEnumOrDefault<TemplateScope>(e.Scope),
                ScopeKey = e.ScopeKey,
                Format = ToEnumOrDefault<TemplateFormat>(e.Format),
                SubjectTemplate = e.SubjectTemplate,
                BodyTemplate = e.BodyTemplate,
                Version = e.Version,
                IsActive = e.IsActive,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            };
        }

        public Task<IReadOnlyList<TemplateDto>> ListAsync(TemplateScope? scope1, string? scope = null, string? scopeKey = null, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        #endregion
    }

    #region DTO / Request placeholders

    //public sealed class TemplateDto
    //{
    //    public string TemplateId { get; set; } = string.Empty;
    //    public string Name { get; set; } = string.Empty;
    //    public string Scope { get; set; } = "Global";
    //    public string? ScopeKey { get; set; }
    //    public string Format { get; set; } = "Html";
    //    public string SubjectTemplate { get; set; } = string.Empty;
    //    public string BodyTemplate { get; set; } = string.Empty;
    //    public int Version { get; set; } = 1;
    //    public bool IsActive { get; set; } = true;
    //    public DateTime CreatedAt { get; set; }
    //    public DateTime? UpdatedAt { get; set; }
    //}

    //public sealed class CreateTemplateRequest
    //{
    //    public string? TemplateId { get; set; }
    //    public string? Name { get; set; }
    //    public string? Scope { get; set; }
    //    public string? ScopeKey { get; set; }
    //    public string? Format { get; set; }
    //    public string? SubjectTemplate { get; set; }
    //    public string? BodyTemplate { get; set; }
    //    public int Version { get; set; } = 1;
    //    public bool IsActive { get; set; } = true;
    //    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    //    public DateTime? UpdatedAt { get; set; }
    //}



    #endregion

    public static class EnumExtensions
    {
        public static TEnum ToEnumOrDefault<TEnum>(this string value, TEnum @default = default) where TEnum : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value)) return @default;
            if (Enum.TryParse<TEnum>(value, true, out var byName) && Enum.IsDefined(typeof(TEnum), byName)) return byName;
            if (long.TryParse(value, out var numeric))
            {
                var obj = Enum.ToObject(typeof(TEnum), numeric);
                if (Enum.IsDefined(typeof(TEnum), obj)) return (TEnum)obj;
            }
            return @default;
        }
    }

}
