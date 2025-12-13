// src/Infrastructure/TestDoubles/SqlInProcessRedis.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Lightweight in-process, in-memory test double that mimics the shape and behavior
    /// of the SQL-backed tables used by the comms stack.
    ///
    /// This class is intended for unit/integration tests where a full SQL Server is not
    /// available. It is NOT a full Redis or SQL emulator; rather it provides a small,
    /// thread-safe surface that mirrors the operations used by the production components:
    ///  - messages (enqueue, read, delete, attempts, status, TTL)
    ///  - dead-letters
    ///  - remote forwards
    ///  - idempotency markers
    ///  - instance registry
    ///  - connections (minimal)
    ///
    /// The implementation favors clarity and predictable semantics over performance.
    /// All public methods are asynchronous to match the production APIs.
    /// </summary>
    public sealed class SqlInProcessRedis : IDisposable, IAsyncDisposable
    {
        // Messages keyed by Id (canonical id)
        private readonly ConcurrentDictionary<string, PatchMessageEntity> _messages = new();
        // Dead letters keyed by Id
        private readonly ConcurrentDictionary<string, DeadLetterEntity> _deadLetters = new();
        // Remote forwards keyed by Id
        private readonly ConcurrentDictionary<string, SqlRemoteForwarder.ForwardRequest> _forwards = new();
        // Idempotency markers keyed by key -> (messageId, ttlUnixSeconds)
        private readonly ConcurrentDictionary<string, (string MessageId, long? TtlUnixSeconds, long CreatedAtUtcTicks)> _idempotency = new();
        // Instances keyed by instance id
        private readonly ConcurrentDictionary<string, IPatchInstanceRegistry.InstanceInfo> _instances = new();
        // Connections keyed by id (minimal representation)
        private readonly ConcurrentDictionary<string, PatchConnectionEntity> _connections = new();

        private bool _disposed;

        public SqlInProcessRedis()
        {
        }

        #region Messages

        /// <summary>
        /// Insert or upsert a message. If idempotencyKey is provided and exists, returns existing id.
        /// Otherwise inserts and returns canonical id.
        /// </summary>
        public Task<string> EnqueueMessageAsync(
            string channel,
            string messageId,
            string payload,
            string? idempotencyKey = null,
            TimeSpan? retention = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            ThrowIfDisposed();

            var canonicalId = MakeMessageId(channel, messageId);
            var nowTicks = DateTime.UtcNow.Ticks;
            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                // Try to create idempotency marker atomically
                var created = _idempotency.GetOrAdd(idempotencyKey, _ => (canonicalId, ttl, nowTicks));
                if (created.MessageId != canonicalId)
                {
                    // Another message already registered for this key
                    return Task.FromResult(created.MessageId);
                }
            }

            var entity = new PatchMessageEntity
            {
                Id = canonicalId,
                Channel = channel ?? string.Empty,
                MessageId = messageId,
                Payload = payload,
                CreatedAtUtcTicks = nowTicks,
                Attempts = 0,
                TtlUnixSeconds = ttl,
                Status = null
            };

            _messages.AddOrUpdate(canonicalId, entity, (_, __) => entity);
            return Task.FromResult(canonicalId);
        }

        /// <summary>
        /// Enqueue a batch of messages. Returns canonical ids in same order.
        /// </summary>
        public Task<IReadOnlyList<string>> EnqueueMessagesBatchAsync(
            IEnumerable<(string? Channel, string? MessageId, string Payload, string? IdempotencyKey, TimeSpan? Retention)> messages,
            CancellationToken ct = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            ThrowIfDisposed();

            var results = new List<string>();
            foreach (var m in messages)
            {
                var channel = m.Channel ?? string.Empty;
                var msgId = string.IsNullOrWhiteSpace(m.MessageId) ? Guid.NewGuid().ToString("N") : m.MessageId;
                var id = MakeMessageId(channel, msgId);
                // Reuse single-message logic (synchronous)
                var _ = EnqueueMessageAsync(channel, msgId, m.Payload, m.IdempotencyKey, m.Retention, ct).GetAwaiter().GetResult();
                results.Add(_);
            }

            return Task.FromResult((IReadOnlyList<string>)results);
        }

        /// <summary>
        /// Try to get a message by canonical id.
        /// </summary>
        public Task<PatchMessageEntity?> TryGetMessageAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            _messages.TryGetValue(id, out var entity);
            return Task.FromResult(entity);
        }

        /// <summary>
        /// Increment attempts for a message and optionally move to dead-letter when threshold reached.
        /// Returns attempts after increment or null when message not found.
        /// </summary>
        public Task<int?> IncrementAttemptsAsync(string id, int maxAttempts, string? failureReason = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            if (!_messages.TryGetValue(id, out var msg)) return Task.FromResult<int?>(null);

            var updated = new PatchMessageEntity
            {
                Id = msg.Id,
                Channel = msg.Channel,
                MessageId = msg.MessageId,
                Payload = msg.Payload,
                CreatedAtUtcTicks = msg.CreatedAtUtcTicks,
                Attempts = msg.Attempts + 1,
                TtlUnixSeconds = msg.TtlUnixSeconds,
                Status = msg.Status
            };

            _messages[id] = updated;

            if (updated.Attempts >= maxAttempts)
            {
                // move to dead-letter
                var dlId = MakeDeadLetterId(updated.Channel, updated.MessageId);
                var dl = new DeadLetterEntity
                {
                    Id = dlId,
                    OriginalMessageId = updated.MessageId,
                    Channel = updated.Channel,
                    Payload = updated.Payload,
                    Reason = failureReason,
                    FailedAtUtcTicks = DateTime.UtcNow.Ticks,
                    Attempts = updated.Attempts,
                    CreatedAtUtcTicks = DateTime.UtcNow.Ticks,
                    TtlUnixSeconds = null,
                    MetadataJson = null
                };

                _deadLetters[dlId] = dl;
                _messages.TryRemove(id, out _);
            }

            return Task.FromResult<int?>(updated.Attempts);
        }

        /// <summary>
        /// Mark message as delivered (set status and optional TTL).
        /// </summary>
        public Task<bool> MarkDeliveredAsync(string id, TimeSpan? retention = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            if (!_messages.TryGetValue(id, out var msg)) return Task.FromResult(false);

            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            var updated = new PatchMessageEntity
            {
                Id = msg.Id,
                Channel = msg.Channel,
                MessageId = msg.MessageId,
                Payload = msg.Payload,
                CreatedAtUtcTicks = msg.CreatedAtUtcTicks,
                Attempts = msg.Attempts,
                TtlUnixSeconds = ttl,
                Status = "delivered",
                DeliveredAtUtcTicks = DateTime.UtcNow.Ticks
            };

            _messages[id] = updated;
            return Task.FromResult(true);
        }

        /// <summary>
        /// Delete a message by id.
        /// </summary>
        public Task<bool> DeleteMessageAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            return Task.FromResult(_messages.TryRemove(id, out _));
        }

        /// <summary>
        /// Enumerate pending messages (simple filter).
        /// </summary>
        public Task<IReadOnlyList<PatchMessageEntity>> QueryPendingMessagesAsync(int limit = 50, int maxAttempts = int.MaxValue, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var list = _messages.Values
                .Where(m => (m.Status == null || m.Status == "queued") &&
                            (m.TtlUnixSeconds == null || m.TtlUnixSeconds > nowUnix) &&
                            m.Attempts < maxAttempts)
                .OrderBy(m => m.CreatedAtUtcTicks)
                .Take(limit)
                .ToList();

            return Task.FromResult((IReadOnlyList<PatchMessageEntity>)list);
        }

        #endregion

        #region Dead letters

        /// <summary>
        /// Query dead letters.
        /// </summary>
        public Task<IReadOnlyList<DeadLetterEntity>> QueryDeadLettersAsync(int limit = 50, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var list = _deadLetters.Values.OrderBy(d => d.CreatedAtUtcTicks).Take(limit).ToList();
            return Task.FromResult((IReadOnlyList<DeadLetterEntity>)list);
        }

        #endregion

        #region Remote forwards

        public Task<string> EnqueueForwardAsync(PatchMessageEntity message, string targetInstanceId, string? idempotencyKey = null, TimeSpan? retention = null, CancellationToken ct = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            ThrowIfDisposed();

            var forwardId = SqlRemoteForwarder.MakeForwardId(targetInstanceId, message.MessageId);
            var nowTicks = DateTime.UtcNow.Ticks;
            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            var req = new SqlRemoteForwarder.ForwardRequest
            {
                Id = forwardId,
                TargetInstanceId = targetInstanceId,
                PayloadJson = JsonSerializer.Serialize(message),
                Message = message,
                CreatedAtUtcTicks = nowTicks,
                Attempts = 0,
                LastAttemptAtUtcTicks = null,
                Status = null,
                TtlUnixSeconds = ttl
            };

            _forwards.AddOrUpdate(forwardId, req, (_, __) => req);
            return Task.FromResult(forwardId);
        }

        public Task<IReadOnlyList<SqlRemoteForwarder.ForwardRequest>> QueryPendingForwardsAsync(string targetInstanceId, int limit = 50, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            ThrowIfDisposed();

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var list = _forwards.Values
                .Where(f => f.TargetInstanceId == targetInstanceId &&
                            (f.Status == null || f.Status == "pending" || f.Status == "queued") &&
                            (f.TtlUnixSeconds == null || f.TtlUnixSeconds > nowUnix))
                .OrderBy(f => f.CreatedAtUtcTicks)
                .Take(limit)
                .ToList();

            return Task.FromResult((IReadOnlyList<SqlRemoteForwarder.ForwardRequest>)list);
        }

        public Task<bool> RemoveForwardAsync(string forwardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(forwardId)) throw new ArgumentNullException(nameof(forwardId));
            ThrowIfDisposed();

            return Task.FromResult(_forwards.TryRemove(forwardId, out _));
        }

        #endregion

        #region Idempotency markers

        public Task<string?> TryGetIdempotencyMessageIdAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            ThrowIfDisposed();

            if (!_idempotency.TryGetValue(key, out var entry)) return Task.FromResult<string?>(null);

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (entry.TtlUnixSeconds.HasValue && entry.TtlUnixSeconds.Value <= nowUnix)
            {
                // expired
                _idempotency.TryRemove(key, out _);
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(entry.MessageId);
        }

        public Task<(bool Created, string? ExistingMessageId)> TryCreateIdempotencyMarkerAsync(string key, string messageId, TimeSpan? retention = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            ThrowIfDisposed();

            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            var createdAt = DateTime.UtcNow.Ticks;

            var added = _idempotency.GetOrAdd(key, _ => (messageId, ttl, createdAt));
            if (added.MessageId == messageId)
                return Task.FromResult((true, (string?)null));

            // existing different message id
            // check expiry
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (added.TtlUnixSeconds.HasValue && added.TtlUnixSeconds.Value <= nowUnix)
            {
                // expired: try to replace
                _idempotency.TryRemove(key, out _);
                var replaced = _idempotency.GetOrAdd(key, _ => (messageId, ttl, createdAt));
                if (replaced.MessageId == messageId) return Task.FromResult((true, (string?)null));
            }

            return Task.FromResult((false, added.MessageId));
        }

        public Task<bool> RemoveIdempotencyMarkerAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            ThrowIfDisposed();

            return Task.FromResult(_idempotency.TryRemove(key, out _));
        }

        #endregion

        #region Instance registry

        public Task<IPatchInstanceRegistry.InstanceInfo> RegisterInstanceAsync(string instanceId, string? hostname = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            ThrowIfDisposed();

            var nowTicks = DateTime.UtcNow.Ticks;
            var info = new IPatchInstanceRegistry.InstanceInfo
            {
                InstanceId = instanceId,
                Hostname = hostname,
                StartedAtUtcTicks = nowTicks,
                LastHeartbeatUtcTicks = nowTicks,
                OwnerInstanceId = null,
                Metadata = metadata
            };

            _instances.AddOrUpdate(instanceId, info, (_, __) =>
            {
                // update heartbeat/hostname/metadata
                return info with
                {
                    Hostname = hostname,
                    LastHeartbeatUtcTicks = nowTicks,
                    Metadata = metadata
                };
            });

            return Task.FromResult(info);
        }

        public Task<IPatchInstanceRegistry.InstanceInfo?> HeartbeatInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            ThrowIfDisposed();

            if (!_instances.TryGetValue(instanceId, out var existing)) return Task.FromResult<IPatchInstanceRegistry.InstanceInfo?>(null);

            var updated = existing with { LastHeartbeatUtcTicks = DateTime.UtcNow.Ticks };
            _instances[instanceId] = updated;
            return Task.FromResult<IPatchInstanceRegistry.InstanceInfo?>(updated);
        }

        public Task<IPatchInstanceRegistry.InstanceInfo?> TryGetInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            ThrowIfDisposed();

            _instances.TryGetValue(instanceId, out var info);
            return Task.FromResult(info);
        }

        public Task<IReadOnlyList<IPatchInstanceRegistry.InstanceInfo>> ListInstancesAsync(bool includeStale = false, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (includeStale) return Task.FromResult((IReadOnlyList<IPatchInstanceRegistry.InstanceInfo>)_instances.Values.ToList());

            var cutoff = DateTime.UtcNow.AddMinutes(-5).Ticks;
            var list = _instances.Values.Where(i => i.LastHeartbeatUtcTicks >= cutoff).ToList();
            return Task.FromResult((IReadOnlyList<IPatchInstanceRegistry.InstanceInfo>)list);
        }

        public Task<bool> TryClaimInstanceAsync(string targetInstanceId, string ownerInstanceId, string? expectedOwnerInstanceId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (string.IsNullOrWhiteSpace(ownerInstanceId)) throw new ArgumentNullException(nameof(ownerInstanceId));
            ThrowIfDisposed();

            if (!_instances.TryGetValue(targetInstanceId, out var info)) return Task.FromResult(false);

            // optimistic claim semantics
            if (expectedOwnerInstanceId == null)
            {
                if (!string.IsNullOrEmpty(info.OwnerInstanceId)) return Task.FromResult(false);
            }
            else
            {
                if (info.OwnerInstanceId != expectedOwnerInstanceId) return Task.FromResult(false);
            }

            var updated = info with { OwnerInstanceId = ownerInstanceId };
            _instances[targetInstanceId] = updated;
            return Task.FromResult(true);
        }

        public Task<bool> ReleaseClaimAsync(string targetInstanceId, string ownerInstanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (string.IsNullOrWhiteSpace(ownerInstanceId)) throw new ArgumentNullException(nameof(ownerInstanceId));
            ThrowIfDisposed();

            if (!_instances.TryGetValue(targetInstanceId, out var info)) return Task.FromResult(false);
            if (info.OwnerInstanceId != ownerInstanceId) return Task.FromResult(false);

            var updated = info with { OwnerInstanceId = null };
            _instances[targetInstanceId] = updated;
            return Task.FromResult(true);
        }

        public Task<bool> RemoveInstanceAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            ThrowIfDisposed();

            return Task.FromResult(_instances.TryRemove(instanceId, out _));
        }

        public Task<IReadOnlyList<string>> RemoveStaleInstancesAsync(TimeSpan staleThreshold, CancellationToken ct = default)
        {
            if (staleThreshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleThreshold));
            ThrowIfDisposed();

            var cutoff = DateTime.UtcNow.Add(-staleThreshold).Ticks;
            var removed = new List<string>();
            foreach (var kv in _instances)
            {
                if (kv.Value.LastHeartbeatUtcTicks < cutoff)
                {
                    if (_instances.TryRemove(kv.Key, out _))
                        removed.Add(kv.Key);
                }
            }

            return Task.FromResult((IReadOnlyList<string>)removed);
        }

        #endregion

        #region Connections (minimal)

        public Task UpsertConnectionAsync(PatchConnectionEntity entity, CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            ThrowIfDisposed();

            _connections[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task<PatchConnectionEntity?> GetConnectionAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            _connections.TryGetValue(id, out var e);
            return Task.FromResult(e);
        }

        public Task<IReadOnlyList<PatchConnectionEntity>> QueryConnectionsByUserAsync(string userId, int limit = 50, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var list = _connections.Values.Where(c => c.UserId == userId).OrderBy(c => c.CreatedAtUtcTicks).Take(limit).ToList();
            return Task.FromResult((IReadOnlyList<PatchConnectionEntity>)list);
        }

        public Task<bool> DeleteConnectionAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            ThrowIfDisposed();

            return Task.FromResult(_connections.TryRemove(id, out _));
        }

        #endregion

        #region Helpers / Utilities

        private static string MakeMessageId(string? channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrWhiteSpace(channel)) return $"msg:{messageId}";
            return $"msg:{channel}:{messageId}";
        }

        private static string MakeDeadLetterId(string? channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channel)) return $"dl:{messageId}";
            return $"dl:{channel}:{messageId}";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInProcessRedis));
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            _disposed = true;
            // clear collections to free memory in long-running test hosts
            _messages.Clear();
            _deadLetters.Clear();
            _forwards.Clear();
            _idempotency.Clear();
            _instances.Clear();
            _connections.Clear();
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        #endregion

        #region Internal helper types

        /// <summary>
        /// Minimal dead-letter entity used by the in-process store.
        /// Mirrors the shape expected by the SQL dead-letter table.
        /// </summary>
        public sealed class DeadLetterEntity
        {
            public string Id { get; init; } = string.Empty;
            public string OriginalMessageId { get; init; } = string.Empty;
            public string? Channel { get; init; }
            public string Payload { get; init; } = string.Empty;
            public string? Reason { get; init; }
            public long FailedAtUtcTicks { get; init; }
            public int Attempts { get; init; }
            public long CreatedAtUtcTicks { get; init; }
            public long? TtlUnixSeconds { get; init; }
            public string? MetadataJson { get; init; }
        }

        #endregion
    }
}
