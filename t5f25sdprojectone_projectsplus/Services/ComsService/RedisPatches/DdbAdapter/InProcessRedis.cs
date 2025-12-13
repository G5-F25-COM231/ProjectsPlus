// src/Infrastructure/Redis/InProcessRedis.cs
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Lightweight in-process adapter used for local development and tests.
    /// Implements a minimal subset of the Redis-like API used by the comms stack:
    /// Publish/Subscribe, simple key/value, and set operations.
    /// Not durable and not distributed — intended only for single-process scenarios.
    /// </summary>
    public sealed class InProcessRedis : IDisposable
    {
        // channel -> set of handlers
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Action<string>, byte>> _subs
            = new(StringComparer.Ordinal);

        // simple key/value store
        private readonly ConcurrentDictionary<string, string> _kv = new(StringComparer.Ordinal);

        // sets: setKey -> members
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _sets
            = new(StringComparer.Ordinal);

        private static readonly byte _marker = 0;
        private bool _disposed;

        public InProcessRedis()
        {
        }

        /// <summary>
        /// Publish a message to a channel. Returns number of local subscribers invoked.
        /// Handlers are invoked fire-and-forget on thread-pool tasks.
        /// </summary>
        public Task<long> PublishAsync(string channel, string message, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (message is null) throw new ArgumentNullException(nameof(message));

            if (!_subs.TryGetValue(channel, out var handlers) || handlers.IsEmpty)
                return Task.FromResult(0L);

            var handlersSnapshot = handlers.Keys.ToArray();
            foreach (var h in handlersSnapshot)
            {
                // fire-and-forget but observe cancellation token
                _ = Task.Run(() =>
                {
                    try
                    {
                        if (!ct.IsCancellationRequested) h(message);
                    }
                    catch
                    {
                        // swallow handler exceptions to avoid crashing publisher
                    }
                }, ct);
            }

            return Task.FromResult((long)handlersSnapshot.Length);
        }

        /// <summary>
        /// Subscribe to a channel. Returns IDisposable to unsubscribe.
        /// Handler is Action<string> to match existing code that invokes handlers synchronously.
        /// </summary>
        public IDisposable Subscribe(string channel, Action<string> handler)
        {
            ThrowIfDisposed();
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            var set = _subs.GetOrAdd(channel, _ => new ConcurrentDictionary<Action<string>, byte>());
            set[handler] = _marker;

            return new Unsubscriber(this, channel, handler);
        }

        /// <summary>
        /// Unsubscribe a handler from a channel.
        /// </summary>
        public void Unsubscribe(string channel, Action<string> handler)
        {
            ThrowIfDisposed();
            if (channel is null) throw new ArgumentNullException(nameof(channel));
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            if (_subs.TryGetValue(channel, out var set))
            {
                set.TryRemove(handler, out _);
                if (set.IsEmpty) _subs.TryRemove(channel, out _);
            }
        }

        /// <summary>
        /// Simple key/value set.
        /// </summary>
        public Task SetKeyAsync(string key, string value, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key is null) throw new ArgumentNullException(nameof(key));
            if (value is null) throw new ArgumentNullException(nameof(value));

            _kv[key] = value;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Simple key/value get. Returns null if not present.
        /// </summary>
        public Task<string?> GetKeyAsync(string key, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key is null) throw new ArgumentNullException(nameof(key));

            return Task.FromResult(_kv.TryGetValue(key, out var v) ? v : null);
        }

        /// <summary>
        /// Add member to a named set.
        /// </summary>
        public Task AddToSetAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey is null) throw new ArgumentNullException(nameof(setKey));
            if (member is null) throw new ArgumentNullException(nameof(member));

            var set = _sets.GetOrAdd(setKey, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            set[member] = _marker;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove member from a named set.
        /// </summary>
        public Task RemoveFromSetAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey is null) throw new ArgumentNullException(nameof(setKey));
            if (member is null) throw new ArgumentNullException(nameof(member));

            if (_sets.TryGetValue(setKey, out var set))
            {
                set.TryRemove(member, out _);
                if (set.IsEmpty) _sets.TryRemove(setKey, out _);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Get members of a named set. Returns empty array if none.
        /// </summary>
        public Task<string[]> GetSetMembersAsync(string setKey, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey is null) throw new ArgumentNullException(nameof(setKey));

            if (_sets.TryGetValue(setKey, out var set))
                return Task.FromResult(set.Keys.ToArray());

            return Task.FromResult(Array.Empty<string>());
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InProcessRedis));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _subs.Clear();
            _kv.Clear();
            _sets.Clear();
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly InProcessRedis _parent;
            private readonly string _channel;
            private readonly Action<string> _handler;
            private bool _disposed;

            public Unsubscriber(InProcessRedis parent, string channel, Action<string> handler)
            {
                _parent = parent;
                _channel = channel;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    _parent.Unsubscribe(_channel, _handler);
                }
                catch
                {
                    // swallow on dispose
                }
            }
        }
    }
}
