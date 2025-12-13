// src/Infrastructure/IRedisClient.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches
{
    /// <summary>
    /// Minimal Redis-like abstraction used across the codebase.
    /// Implementations: DdbAdapter, MssqlAdapter, InProcessAdapter, RedisNetAdapter, etc.
    /// All methods are async-friendly and designed to be durable when paired with the
    /// "persist-first" pattern in the application (DB commit before adapter calls).
    /// </summary>
    public interface IRedisClient : IDisposable
    {
        // Strings
        Task<bool> StringSetAsync(string key, string value, CancellationToken ct = default);
        Task<string?> StringGetAsync(string key, CancellationToken ct = default);
        Task<bool> KeyDeleteAsync(string key, CancellationToken ct = default);

        // Sets
        Task<long> SetAddAsync(string setKey, string member, CancellationToken ct = default);
        Task<long> SetRemoveAsync(string setKey, string member, CancellationToken ct = default);
        Task<string[]> SetMembersAsync(string setKey, CancellationToken ct = default);
        Task<bool> SetContainsAsync(string setKey, string member, CancellationToken ct = default);

        // Pub/Sub
        /// <summary>
        /// Publish a message to a channel. Returns number of subscribers (best-effort).
        /// Implementations should perform publish asynchronously and not block the caller.
        /// </summary>
        Task<long> PublishAsync(string channel, string message, CancellationToken ct = default);

        /// <summary>
        /// Subscribe to a channel. Returns IDisposable to unsubscribe.
        /// Handler is invoked asynchronously by the adapter; handlers must be resilient.
        /// </summary>
        IDisposable Subscribe(string channel, Action<string> handler);
    }
}
