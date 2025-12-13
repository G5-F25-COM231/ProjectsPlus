// src/ProjectsPlus.Comms/Persistence/UserRepository.Ef.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public interface IUserRepository
    {
        Task<UserDto?> GetByIdAsync(long userId, CancellationToken ct = default);
        Task<UserDto?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);
        Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct = default);
        Task UpdateAsync(long userId, Action<UserDto> applyChanges, CancellationToken ct = default);
        Task SetPresenceAsync(long userId, string presence, DateTimeOffset seenAt, CancellationToken ct = default);
        Task AddOrUpdateDeviceTokenAsync(long userId, string token, string platform, CancellationToken ct = default);
        Task RemoveDeviceTokenAsync(long userId, string token, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetActiveConnectionIdsAsync(long userId, CancellationToken ct = default);
    }

    public class UserRepository : IUserRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public UserRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<UserDto?> GetByIdAsync(long userId, CancellationToken ct = default)
        {
            var e = await _db.Users!.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            return e == null ? null : MapToDto(e);
        }

        public async Task<UserDto?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(normalizedEmail)) return null;
            var e = await _db.Users!.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && !u.IsDeleted, ct);
            return e == null ? null : MapToDto(e);
        }

        public async Task<UserDto> CreateAsync(CreateUserRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var entity = new UserEntity
            {
                Id = req.UserId == 0 ? 0 : req.UserId, // if zero, DB will assign if identity; otherwise use provided
                Email = req.Email ?? string.Empty,
                NormalizedEmail = req.NormalizedEmail ?? (req.Email ?? string.Empty).ToUpperInvariant(),
                DisplayName = req.DisplayName,
                FirstName = req.FirstName,
                LastName = req.LastName,
                ProviderId = req.ProviderId,
                PasswordHash = req.PasswordHash,
                PasswordSalt = req.PasswordSalt,
                AttributesJson = req.Attributes == null ? null : JsonSerializer.Serialize(req.Attributes),
                ProfileJson = req.Profile == null ? null : JsonSerializer.Serialize(req.Profile),
                SystemTypeId = req.SystemTypeId,
                IsActive = req.IsActive,
                IsDeleted = false,
                CreatedBy = req.CreatedBy,
                CreatedAt = req.CreatedAt == default ? DateTimeOffset.UtcNow : req.CreatedAt,
                UpdatedAt = req.UpdatedAt == default ? DateTimeOffset.UtcNow : req.UpdatedAt,
                Version = 1,
                LastSeenUtc = req.LastSeenUtc,
                PresenceStatus = req.PresenceStatus,
                ActiveConnectionIdsJson = req.ActiveConnectionIds == null ? null : JsonSerializer.Serialize(req.ActiveConnectionIds),
                NotificationPreferencesJson = req.NotificationPreferences == null ? null : JsonSerializer.Serialize(req.NotificationPreferences),
                DeviceTokensJson = req.DeviceTokens == null ? null : JsonSerializer.Serialize(req.DeviceTokens)
            };

            await _db.Users!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);

            return MapToDto(entity);
        }

        public async Task UpdateAsync(long userId, Action<UserDto> applyChanges, CancellationToken ct = default)
        {
            if (applyChanges == null) throw new ArgumentNullException(nameof(applyChanges));

            var e = await _db.Users!.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (e == null) throw new InvalidOperationException("User not found");

            var dto = MapToDto(e);
            applyChanges(dto);

            // apply back to entity
            e.DisplayName = dto.DisplayName;
            e.FirstName = dto.FirstName;
            e.LastName = dto.LastName;
            e.IsActive = dto.IsActive;
            e.AttributesJson = dto.Attributes == null ? null : JsonSerializer.Serialize(dto.Attributes);
            e.ProfileJson = dto.Profile == null ? null : JsonSerializer.Serialize(dto.Profile);
            e.UpdatedAt = DateTimeOffset.UtcNow;
            e.Version = e.Version + 1;

            _db.Users.Update(e);
            await _db.SaveChangesAsync(ct);
        }

        public async Task SetPresenceAsync(long userId, string presence, DateTimeOffset seenAt, CancellationToken ct = default)
        {
            var e = await _db.Users!.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (e == null) return;

            e.PresenceStatus = presence;
            e.LastSeenUtc = seenAt;
            e.UpdatedAt = DateTimeOffset.UtcNow;
            _db.Users.Update(e);
            await _db.SaveChangesAsync(ct);
        }

        public async Task AddOrUpdateDeviceTokenAsync(long userId, string token, string platform, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            var e = await _db.Users!.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (e == null) throw new InvalidOperationException("User not found");

            var list = new List<Dictionary<string, string>>();
            if (!string.IsNullOrWhiteSpace(e.DeviceTokensJson))
            {
                try { list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(e.DeviceTokensJson) ?? new List<Dictionary<string, string>>(); }
                catch { list = new List<Dictionary<string, string>>(); }
            }

            var existing = list.Find(d => d.TryGetValue("token", out var t) && t == token);
            if (existing != null)
            {
                existing["platform"] = platform;
            }
            else
            {
                list.Add(new Dictionary<string, string> { ["token"] = token, ["platform"] = platform });
            }

            e.DeviceTokensJson = JsonSerializer.Serialize(list);
            e.UpdatedAt = DateTimeOffset.UtcNow;
            _db.Users.Update(e);
            await _db.SaveChangesAsync(ct);
        }

        public async Task RemoveDeviceTokenAsync(long userId, string token, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            var e = await _db.Users!.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (e == null) return;

            if (string.IsNullOrWhiteSpace(e.DeviceTokensJson)) return;

            try
            {
                var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(e.DeviceTokensJson) ?? new List<Dictionary<string, string>>();
                var removed = list.RemoveAll(d => d.TryGetValue("token", out var t) && t == token);
                e.DeviceTokensJson = list.Count == 0 ? null : JsonSerializer.Serialize(list);
                if (removed > 0)
                {
                    e.UpdatedAt = DateTimeOffset.UtcNow;
                    _db.Users.Update(e);
                    await _db.SaveChangesAsync(ct);
                }
            }
            catch
            {
                // ignore malformed JSON
            }
        }

        public async Task<IReadOnlyList<string>> GetActiveConnectionIdsAsync(long userId, CancellationToken ct = default)
        {
            var e = await _db.Users!.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
            if (e == null || string.IsNullOrWhiteSpace(e.ActiveConnectionIdsJson)) return Array.Empty<string>();

            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(e.ActiveConnectionIdsJson);
                return list ?? Array.Empty<string>().ToList();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        #region Mapping helpers

        private static UserDto MapToDto(UserEntity e)
        {
            IDictionary<string, object?>? attrs = null;
            if (!string.IsNullOrWhiteSpace(e.AttributesJson))
            {
                try { attrs = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.AttributesJson); }
                catch { attrs = null; }
            }

            IDictionary<string, object?>? profile = null;
            if (!string.IsNullOrWhiteSpace(e.ProfileJson))
            {
                try { profile = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.ProfileJson); }
                catch { profile = null; }
            }

            List<Dictionary<string, string>>? deviceTokens = null;
            if (!string.IsNullOrWhiteSpace(e.DeviceTokensJson))
            {
                try { deviceTokens = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(e.DeviceTokensJson); }
                catch { deviceTokens = null; }
            }

            List<string>? connections = null;
            if (!string.IsNullOrWhiteSpace(e.ActiveConnectionIdsJson))
            {
                try { connections = JsonSerializer.Deserialize<List<string>>(e.ActiveConnectionIdsJson); }
                catch { connections = null; }
            }

            IDictionary<string, object?>? notifPrefs = null;
            if (!string.IsNullOrWhiteSpace(e.NotificationPreferencesJson))
            {
                try { notifPrefs = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.NotificationPreferencesJson); }
                catch { notifPrefs = null; }
            }

            return new UserDto
            {
                UserId = e.Id,
                Email = e.Email,
                NormalizedEmail = e.NormalizedEmail,
                DisplayName = e.DisplayName,
                FirstName = e.FirstName,
                LastName = e.LastName,
                IsActive = e.IsActive,
                IsDeleted = e.IsDeleted,
                Attributes = attrs,
                Profile = profile,
                DeviceTokens = deviceTokens,
                ActiveConnectionIds = connections,
                NotificationPreferences = notifPrefs,
                LastSeenUtc = e.LastSeenUtc,
                PresenceStatus = e.PresenceStatus,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
                Version = e.Version
            };
        }

        #endregion
    }

    #region DTO / Request placeholders

    public sealed class UserDto
    {
        public long UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string NormalizedEmail { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public IDictionary<string, object?>? Attributes { get; set; }
        public IDictionary<string, object?>? Profile { get; set; }
        public List<Dictionary<string, string>>? DeviceTokens { get; set; }
        public List<string>? ActiveConnectionIds { get; set; }
        public IDictionary<string, object?>? NotificationPreferences { get; set; }
        public DateTimeOffset? LastSeenUtc { get; set; }
        public string? PresenceStatus { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public int Version { get; set; }
    }

    public sealed class CreateUserRequest
    {
        public long UserId { get; set; }
        public string? Email { get; set; }
        public string? NormalizedEmail { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? ProviderId { get; set; }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        public IDictionary<string, object?>? Attributes { get; set; }
        public IDictionary<string, object?>? Profile { get; set; }
        public long? SystemTypeId { get; set; }
        public bool IsActive { get; set; } = true;
        public long? CreatedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSeenUtc { get; set; }
        public string? PresenceStatus { get; set; }
        public List<string>? ActiveConnectionIds { get; set; }
        public IDictionary<string, object?>? NotificationPreferences { get; set; }
        public List<Dictionary<string, string>>? DeviceTokens { get; set; }
    }

    #endregion
}
