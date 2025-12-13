// src/Services/Authorization/PolicyAuthorizationService.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Authorization.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.Authorization
{
    public class PolicyAuthorizationService : IAuthorizationService
    {
        private readonly IAuthorizationRepository _repo;
        private readonly IMemoryCache _cache;
        private readonly AuthorizationOptions _options;

        public PolicyAuthorizationService(IAuthorizationRepository repo, IMemoryCache cache, IOptions<AuthorizationOptions> options)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options?.Value ?? new AuthorizationOptions();
        }

        public async Task<bool> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default)
        {
            if (userId <= 0) return false;

            // Admin bypass
            if (await _repo.UserHasRoleAsync(userId, _options.AdminRoleName, ct).ConfigureAwait(false)) return true;

            var perms = await GetEffectivePermissionsForUserAsync(userId, ct).ConfigureAwait(false);
            return perms.Contains(action, StringComparer.OrdinalIgnoreCase);
        }

        public Task<bool> CanViewProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.View", "Project", projectId, ct);

        public Task<bool> CanEditProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.Edit", "Project", projectId, ct);

        public Task<bool> IsAuthenticatedAsync(long userId, CancellationToken ct = default)
            => Task.FromResult(userId > 0);

        public async Task<IReadOnlyDictionary<long, bool>> IsAuthorizedForManyAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default)
        {
            var allowed = await IsAuthorizedAsync(userId, action, resourceType, null, ct).ConfigureAwait(false);
            var result = new Dictionary<long, bool>(resourceIds.Count);
            foreach (var id in resourceIds) result[id] = allowed;
            return result;
        }

        public Task<bool> CanPerformGlobalActionAsync(long userId, string action, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, action, null, null, ct);

        private Task<HashSet<string>> GetEffectivePermissionsForUserAsync(long userId, CancellationToken ct)
        {
            var cacheKey = $"user_perms:{userId}";
            if (_cache.TryGetValue(cacheKey, out HashSet<string> cached)) return Task.FromResult(cached);

            return FetchAndCachePermissionsAsync(userId, cacheKey, ct);
        }

        private async Task<HashSet<string>> FetchAndCachePermissionsAsync(long userId, string cacheKey, CancellationToken ct)
        {
            var permissions = await _repo.GetPermissionsForUserAsync(userId, ct).ConfigureAwait(false);
            var set = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);

            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.CacheDurationSeconds)
            };
            _cache.Set(cacheKey, set, cacheOptions);

            return set;
        }
    }
}
