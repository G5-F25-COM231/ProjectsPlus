// src/Infrastructure/Instances/IPatchInstanceRegistry.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Abstraction for a process/instance registry used by the comms stack.
    /// Implementations track active instances (processes) that can own connections,
    /// perform work, or be targeted for forwarding. Typical backing stores include
    /// DynamoDB, SQL, Redis, or in-memory test doubles.
    /// 
    /// The registry is responsible for:
    ///  - registering an instance (with optional metadata),
    ///  - heartbeating to indicate liveness,
    ///  - listing and querying instances,
    ///  - claiming/releasing ownership for routing/forwarding,
    ///  - removing stale entries.
    /// 
    /// Implementations should be safe for concurrent use.
    /// </summary>
    public interface IPatchInstanceRegistry : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Lightweight information about a registered instance.
        /// </summary>
        public sealed record InstanceInfo
        {
            /// <summary>
            /// Canonical instance id (e.g., GUID or machine name).
            /// </summary>
            public string InstanceId { get; init; } = string.Empty;

            /// <summary>
            /// Optional human-friendly name or host identifier.
            /// </summary>
            public string? Hostname { get; init; }

            /// <summary>
            /// Ticks when the instance was first registered (UTC).
            /// </summary>
            public long StartedAtUtcTicks { get; init; } = DateTime.UtcNow.Ticks;

            /// <summary>
            /// Ticks of the last heartbeat (UTC).
            /// </summary>
            public long LastHeartbeatUtcTicks { get; init; } = DateTime.UtcNow.Ticks;

            /// <summary>
            /// Optional owner/claim id used when this instance is claimed by another coordinator.
            /// </summary>
            public string? OwnerInstanceId { get; init; }

            /// <summary>
            /// Optional arbitrary metadata (string->string) for diagnostics or routing hints.
            /// </summary>
            public IReadOnlyDictionary<string, string>? Metadata { get; init; }

            /// <summary>
            /// Convenience: returns LastHeartbeatUtc as DateTime (UTC).
            /// </summary>
            public DateTime LastHeartbeatUtc => new DateTime(LastHeartbeatUtcTicks, DateTimeKind.Utc);

            /// <summary>
            /// Convenience: returns StartedAtUtc as DateTime (UTC).
            /// </summary>
            public DateTime StartedAtUtc => new DateTime(StartedAtUtcTicks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Register or update an instance entry in the registry.
        /// If an entry for the same instanceId exists it will be updated with the provided info.
        /// Returns the stored InstanceInfo (may include server-assigned fields).
        /// </summary>
        Task<InstanceInfo> RegisterAsync(string instanceId, string? hostname = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default);

        /// <summary>
        /// Send a heartbeat for the given instance id to mark it as alive.
        /// Returns the updated InstanceInfo or null if the instance is not found.
        /// </summary>
        Task<InstanceInfo?> HeartbeatAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Try to get the InstanceInfo for the given instance id.
        /// Returns null when not found.
        /// </summary>
        Task<InstanceInfo?> TryGetAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// List all known instances. Implementations may optionally filter out stale entries.
        /// </summary>
        Task<IReadOnlyList<InstanceInfo>> ListInstancesAsync(bool includeStale = false, CancellationToken ct = default);

        /// <summary>
        /// Attempt to claim the target instance on behalf of the caller (ownerInstanceId).
        /// Returns true when the claim succeeded. Typical semantics:
        ///  - succeed only if the target is unclaimed or matches an expected owner (optimistic).
        ///  - implementations may accept an expectedOwner parameter to enforce conditional claims.
        /// </summary>
        Task<bool> TryClaimAsync(string targetInstanceId, string ownerInstanceId, string? expectedOwnerInstanceId = null, CancellationToken ct = default);

        /// <summary>
        /// Release a previously acquired claim on the target instance.
        /// Returns true when the release succeeded (claim removed or owner changed).
        /// </summary>
        Task<bool> ReleaseClaimAsync(string targetInstanceId, string ownerInstanceId, CancellationToken ct = default);

        /// <summary>
        /// Remove an instance entry from the registry. Returns true when an entry was removed.
        /// Use with care; typically only used during graceful shutdown or administrative cleanup.
        /// </summary>
        Task<bool> RemoveAsync(string instanceId, CancellationToken ct = default);

        /// <summary>
        /// Remove entries considered stale according to the provided threshold.
        /// Returns the list of instance ids that were removed.
        /// </summary>
        Task<IReadOnlyList<string>> RemoveStaleAsync(TimeSpan staleThreshold, CancellationToken ct = default);

        /// <summary>
        /// Optional helper to compute a canonical instance id when callers need to generate one.
        /// Implementations may simply return the provided id or apply a prefix/normalization.
        /// </summary>
        string MakeInstanceId(string rawId);
    }
}
