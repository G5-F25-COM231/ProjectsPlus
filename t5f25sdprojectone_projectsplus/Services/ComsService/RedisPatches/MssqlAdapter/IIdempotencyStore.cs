// src/Infrastructure/Idempotency/IIdempotencyStore.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Abstraction for a simple idempotency marker store.
    /// Implementations should be able to create markers, read existing markers,
    /// remove markers and optionally cleanup expired markers.
    /// </summary>
    public interface IIdempotencyStore : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Try to get the message id associated with the given idempotency key.
        /// Returns null when not found or expired.
        /// </summary>
        Task<string?> TryGetMessageIdAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Try to create an idempotency marker for the given key and messageId.
        /// Returns (Created=true, ExistingMessageId=null) when created.
        /// If a marker already exists, returns (Created=false, ExistingMessageId=theExistingId).
        /// </summary>
        Task<(bool Created, string? ExistingMessageId)> TryCreateMarkerAsync(
            string key,
            string messageId,
            TimeSpan? retention = null,
            CancellationToken ct = default);

        /// <summary>
        /// Remove an idempotency marker by key. Returns true when a row was deleted.
        /// </summary>
        Task<bool> RemoveMarkerAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Remove expired markers. Returns the number of removed rows.
        /// </summary>
        Task<int> CleanupExpiredAsync(CancellationToken ct = default);
    }
}
