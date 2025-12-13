// src/Infrastructure/Registry/IInstanceRegistry.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Lightweight instance registry abstraction.
    /// Responsibilities:
    /// - register/deregister instances and heartbeats
    /// - map connectionId -> ownerInstance
    /// - lookup instances and owners
    /// Implementations: DdbInstanceRegistry, SqlInstanceRegistry.
    /// </summary>
    public interface IInstanceRegistry
    {
        /// <summary>
        /// Basic instance descriptor used by the registry.
        /// </summary>
        public sealed record InstanceInfo(string InstanceId, string Address, DateTime LastHeartbeatUtc, IReadOnlyDictionary<string, string>? Metadata = null);

        /// <summary>
        /// Register or upsert an instance entry.
        /// </summary>
        Task RegisterAsync(InstanceInfo instance, CancellationToken ct = default);

        /// <summary>
        /// Update heartbeat for an instance. Returns true if instance exists and heartbeat was recorded.
        /// </summary>
        Task<bool> HeartbeatAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Deregister an instance and optionally release ownership of any connections it owned.
        /// </summary>
        Task<bool> DeregisterAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Get instance info by id. Returns null if not found.
        /// </summary>
        Task<InstanceInfo?> GetInstanceAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// List all known instances (may be filtered by implementation).
        /// </summary>
        Task<IReadOnlyList<InstanceInfo>> ListInstancesAsync(CancellationToken ct = default);

        /// <summary>
        /// Atomically claim ownership of a connection for an instance.
        /// Returns true if claim succeeded; false if already owned by another instance.
        /// </summary>
        Task<bool> TryClaimConnectionAsync(string connectionId, string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Release ownership of a connection. Returns true if release succeeded (was owned by instanceId).
        /// </summary>
        Task<bool> ReleaseConnectionAsync(string connectionId, string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Get the current owner instance id for a connection, or null if none.
        /// </summary>
        Task<string?> GetOwnerForConnectionAsync(string connectionId, CancellationToken ct = default);

        /// <summary>
        /// Remove stale instances and optionally return the list of removed instance ids.
        /// </summary>
        Task<IReadOnlyList<string>> CleanupStaleInstancesAsync(TimeSpan staleAfter, CancellationToken ct = default);
    }
}
