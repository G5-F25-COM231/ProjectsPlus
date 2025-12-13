// src/Auth/Caching/AuthorizationCache.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace t5f25sdprojectone_projectsplus.Services.Authorization
{
    /// <summary>
    /// Lightweight per-user cache for authorization results or computed permission sets.
    /// - Stores any serializable value (commonly: AuthorizationResult or IReadOnlyCollection&lt;string&gt; of permissions).
    /// - Supports force-refresh by explicitly invalidating a user's cache entry.
    /// - Keeps behavior synchronous under the hood (IMemoryCache) but exposes async-friendly methods.
    /// </summary>
    public class AuthorizationCache
    {
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _ttl;

        /// <summary>
        /// Create a cache wrapper.
        /// - ttl: recommended between 30s and 2m depending on permission-change frequency.
        /// </summary>
        public AuthorizationCache(IMemoryCache memoryCache, TimeSpan ttl)
        {
            _cache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _ttl = ttl;
        }

        private static string Key(long userId) => $"authorise:user:{userId}";

        /// <summary>
        /// Try read a cached value for the user. Returns null when missing.
        /// </summary>
        public Task<T?> GetAsync<T>(long userId, CancellationToken ct = default) where T : class
        {
            if (_cache.TryGetValue(Key(userId), out var val) && val is T typed) return Task.FromResult(typed);
            return Task.FromResult<T?>(null);
        }

        /// <summary>
        /// Set a cache entry for the user. Overwrites previous value.
        /// </summary>
        public Task SetAsync<T>(long userId, T value, CancellationToken ct = default) where T : class
        {
            var opts = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _ttl
            };
            _cache.Set(Key(userId), value, opts);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove the user's cache entry. Call after role/permission changes.
        /// </summary>
        public Task InvalidateAsync(long userId, CancellationToken ct = default)
        {
            _cache.Remove(Key(userId));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Read-or-compute helper: if a cached value exists returns it, otherwise compute using factory, cache it, and return.
        /// Factory is invoked only when entry is missing.
        /// </summary>
        public async Task<T> GetOrAddAsync<T>(long userId, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default) where T : class
        {
            var existing = await GetAsync<T>(userId, ct).ConfigureAwait(false);
            if (existing != null) return existing;

            var computed = await factory(ct).ConfigureAwait(false);
            if (computed != null) await SetAsync(userId, computed, ct).ConfigureAwait(false);
            return computed;
        }
    }
}
