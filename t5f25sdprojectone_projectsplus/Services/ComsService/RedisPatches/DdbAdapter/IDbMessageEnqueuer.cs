// src/Infrastructure/Enqueue/IDbMessageEnqueuer.cs
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Store-specific enqueue API for durable messages.
    /// Implementations: DdbMessageEnqueuer, SqlMessageEnqueuer.
    /// </summary>
    public interface IDbMessageEnqueuer
    {
        /// <summary>
        /// Persist a durable message to be delivered to a target.
        /// Returns the persistent message id (store-specific, e.g. "message:{guid}" or DB PK).
        /// </summary>
        Task<string> EnqueueAsync(string targetType, string targetId, string payload, string? idempotencyKey = null, CancellationToken ct = default);

        /// <summary>
        /// Persist a batch of durable messages. Implementations may batch or transact.
        /// Returns persisted message ids in the same order as the input.
        /// </summary>
        Task<string[]> EnqueueBatchAsync((string targetType, string targetId, string payload, string? idempotencyKey)[] items, CancellationToken ct = default);
    }
}
