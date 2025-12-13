// src/Infrastructure/Enqueue/IPatchMessageEnqueuer.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Abstraction for enqueuing PatchMessageEntity instances into the backing store.
    /// Implementations persist message items (or batches) and return canonical message id(s).
    /// Implementations should be safe for concurrent use and honor idempotency keys when provided.
    /// </summary>
    public interface IPatchMessageEnqueuer : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Enqueue a single message for delivery.
        /// Returns the canonical message id created (or the existing id when an idempotency key is honored).
        /// </summary>
        /// <param name="channel">Optional channel/topic for routing.</param>
        /// <param name="messageId">Message identifier (if null/empty, implementation may generate one).</param>
        /// <param name="payload">Message payload (serialized JSON or other string form).</param>
        /// <param name="idempotencyKey">Optional idempotency key. Repeated calls with same key should return same id.</param>
        /// <param name="retention">Optional retention TTL for the message (relative timespan).</param>
        /// <param name="ct">Cancellation token.</param>
        Task<string> EnqueueAsync(
            string? channel,
            string? messageId,
            string payload,
            string? idempotencyKey = null,
            TimeSpan? retention = null,
            CancellationToken ct = default);

        /// <summary>
        /// Enqueue multiple messages in a single logical operation.
        /// Returns the list of created message ids in the same order as the input messages.
        /// </summary>
        /// <param name="messages">Collection of messages to enqueue. Each tuple is (Channel, MessageId, Payload, optional IdempotencyKey, optional Retention).</param>
        /// <param name="ct">Cancellation token.</param>
        Task<IReadOnlyList<string>> EnqueueBatchAsync(
            IEnumerable<(string? Channel, string? MessageId, string Payload, string? IdempotencyKey, TimeSpan? Retention)> messages,
            CancellationToken ct = default);

        /// <summary>
        /// Optionally remove an idempotency marker for the given key.
        /// Returns true when a marker was removed.
        /// </summary>
        Task<bool> RemoveIdempotencyMarkerAsync(string idempotencyKey, CancellationToken ct = default);

        /// <summary>
        /// Helper to compute a canonical message id for a given channel and message identifier.
        /// Implementations may expose this to allow callers to predict ids when needed.
        /// </summary>
        string MakeMessageId(string? channel, string messageId);
    }
}
