// src/Services/Authorization/AuthorizationService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using t5f25sdprojectone_projectsplus.Services.Authorization.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// Repository-backed authorization service registered as AuthorizationService.
    /// - Implements the IAuthorizationService contract (boolean convenience checks).
    /// - Reads a user's effective permission names from IAuthorizationRepository.
    /// - Supports an Admin-role bypass and per-user IMemoryCache to reduce DB pressure.
    /// - Logs repository failures and cache lifecycle events for operational visibility.
    /// </summary>
    public class AuthorizationService : IAuthorizationService
    {
        private readonly IAuthorizationRepository _repo;
        private readonly IMemoryCache _cache;
        private readonly AuthorizationOptions _options;
        private readonly ILogger<AuthorizationService> _logger;

        public AuthorizationService(
            IAuthorizationRepository repo,
            IMemoryCache cache,
            IOptions<AuthorizationOptions>? options = null,
            ILogger<AuthorizationService>? logger = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options?.Value ?? new AuthorizationOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // Core check required by the IAuthorizationService contract.
        public async Task<bool> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default)
        {
            if (userId <= 0) return false;
            if (string.IsNullOrWhiteSpace(action)) return false;

            // Fast admin bypass if configured
            if (!string.IsNullOrWhiteSpace(_options.AdminRoleName))
            {
                try
                {
                    if (await _repo.UserHasRoleAsync(userId, _options.AdminRoleName, ct).ConfigureAwait(false))
                    {
                        _logger.LogDebug("AuthorizationService: admin bypass for user {UserId} via role {AdminRole}", userId, _options.AdminRoleName);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "AuthorizationService: failed to check admin role '{AdminRole}' for user {UserId}; falling back to permission checks", _options.AdminRoleName, userId);
                }
            }

            // Fetch the user's effective permissions (cached)
            var perms = await GetEffectivePermissionsForUserAsync(userId, ct).ConfigureAwait(false);

            // Permission strings are treated case-insensitively
            var allowed = perms.Contains(action, StringComparer.OrdinalIgnoreCase);
            _logger.LogDebug("AuthorizationService: authorization check user={UserId} action={Action} resourceType={ResourceType} resourceId={ResourceId} allowed={Allowed}", userId, action, resourceType ?? "-", resourceId?.ToString() ?? "-", allowed);
            return allowed;
        }

        // Convenience helpers call through to the core check with canonical action/resource values.
        public Task<bool> CanViewProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.View", "Project", projectId, ct);

        public Task<bool> CanEditProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.Edit", "Project", projectId, ct);

        public Task<bool> CanPerformGlobalActionAsync(long userId, string action, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, action, null, null, ct);

        public Task<bool> IsAuthenticatedAsync(long userId, CancellationToken ct = default)
            => Task.FromResult(userId > 0);

        public async Task<IReadOnlyDictionary<long, bool>> IsAuthorizedForManyAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default)
        {
            // Current, simple semantics: treat permission as global for the user.
            // If allowed globally, every resource id is allowed; otherwise none are.
            var allowed = await IsAuthorizedAsync(userId, action, resourceType, null, ct).ConfigureAwait(false);
            var map = new Dictionary<long, bool>(resourceIds.Count);
            foreach (var id in resourceIds) map[id] = allowed;

            _logger.LogDebug("AuthorizationService: IsAuthorizedForMany user={UserId} action={Action} resourceType={ResourceType} count={Count} allowed={Allowed}", userId, action, resourceType, resourceIds.Count, allowed);
            return map;
        }

        /// <summary>
        /// Invalidate the per-user permission cache so subsequent checks re-fetch from the repository.
        /// Call this after role/permission assignments or administrative changes.
        /// </summary>
        public Task InvalidateUserCacheAsync(long userId)
        {
            _cache.Remove(CacheKey(userId));
            _logger.LogInformation("AuthorizationService: invalidated permission cache for user {UserId}", userId);
            return Task.CompletedTask;
        }

        // -------------------------
        // Internal helpers
        // -------------------------

        private string CacheKey(long userId) => $"auth:perms:{userId}";

        private async Task<HashSet<string>> GetEffectivePermissionsForUserAsync(long userId, CancellationToken ct)
        {
            var key = CacheKey(userId);
            if (_cache.TryGetValue(key, out HashSet<string>? cached))
            {
                _logger.LogDebug("AuthorizationService: cache hit for user {UserId}", userId);
                return cached!;
            }

            IEnumerable<string>? perms = null;
            try
            {
                perms = await _repo.GetPermissionsForUserAsync(userId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuthorizationService: failed to load permissions for user {UserId}; returning empty permission set", userId);
                perms = Array.Empty<string>();
            }

            var set = new HashSet<string>(perms ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(5, _options.CacheDurationSeconds))
            };
            _cache.Set(key, set, options);
            _logger.LogDebug("AuthorizationService: cached {Count} permissions for user {UserId} ttlSeconds={Ttl}", set.Count, userId, _options.CacheDurationSeconds);

            return set;
        }
    }

    /// <summary>
    /// Options used to configure AuthorizationService behavior.
    /// - AdminRoleName: role that bypasses checks (set empty to disable).
    /// - CacheDurationSeconds: TTL for per-user permission cache.
    /// </summary>
    //public class AuthorizationOptions
    //{
    //    public string AdminRoleName { get; set; } = "Admin";
    //    public int CacheDurationSeconds { get; set; } = 60;
    //}
}
