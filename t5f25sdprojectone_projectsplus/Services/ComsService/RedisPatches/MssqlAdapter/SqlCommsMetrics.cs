// src/Infrastructure/Telemetry/SqlCommsMetrics.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Lightweight telemetry helper for SQL-backed comms components.
    ///
    /// This class centralizes Meter, instruments and convenience helpers used across
    /// the SQL comms implementation to record counts, latencies and simple gauges.
    ///
    /// Designed to be safe for use from multiple threads and to be easy to call from
    /// hot paths (small allocation footprint). It intentionally exposes a small set of
    /// semantic methods rather than raw instruments so callers don't need to know metric names.
    /// </summary>
    public sealed class SqlCommsMetrics : IDisposable
    {
        private const string MeterName = "projectsplus.comms.sql";
        private const string MeterVersion = "1.0.0";

        private readonly Meter _meter;

        // Counters
        private readonly Counter<long> _enqueueCounter;
        private readonly Counter<long> _enqueueBatchCounter;
        private readonly Counter<long> _enqueueIdempotentHitCounter;
        private readonly Counter<long> _deliveryAttemptCounter;
        private readonly Counter<long> _deliverySuccessCounter;
        private readonly Counter<long> _deliveryFailureCounter;
        private readonly Counter<long> _forwardAttemptCounter;
        private readonly Counter<long> _forwardSuccessCounter;
        private readonly Counter<long> _forwardFailureCounter;
        private readonly Counter<long> _idempotencyCreateCounter;
        private readonly Counter<long> _idempotencyRemoveCounter;

        // Histograms
        private readonly Histogram<double> _dbCommandDurationMs;
        private readonly Histogram<double> _enqueueLatencyMs;
        private readonly Histogram<double> _deliveryLatencyMs;
        private readonly Histogram<double> _forwardLatencyMs;

        // Observable gauge for active connections (optional provider)
        private readonly ObservableGauge<long> _activeConnectionsGauge;
        private Func<int>? _activeConnectionsProvider;
        private readonly object _providerLock = new object();

        private bool _disposed;

        /// <summary>
        /// Create a new metrics instance. Multiple instances are allowed but typically a single
        /// shared instance is registered in DI and reused.
        /// </summary>
        public SqlCommsMetrics()
        {
            _meter = new Meter(MeterName, MeterVersion);

            // Counters
            _enqueueCounter = _meter.CreateCounter<long>("messages.enqueued", description: "Number of messages enqueued");
            _enqueueBatchCounter = _meter.CreateCounter<long>("messages.enqueued.batch", description: "Number of messages enqueued via batch operations");
            _enqueueIdempotentHitCounter = _meter.CreateCounter<long>("messages.enqueued.idempotency_hit", description: "Number of enqueue attempts that hit an existing idempotency marker");
            _deliveryAttemptCounter = _meter.CreateCounter<long>("messages.delivery.attempts", description: "Delivery attempts for messages");
            _deliverySuccessCounter = _meter.CreateCounter<long>("messages.delivery.success", description: "Successful deliveries");
            _deliveryFailureCounter = _meter.CreateCounter<long>("messages.delivery.failure", description: "Failed deliveries");
            _forwardAttemptCounter = _meter.CreateCounter<long>("messages.forward.attempts", description: "Forward attempts to remote instances");
            _forwardSuccessCounter = _meter.CreateCounter<long>("messages.forward.success", description: "Successful forwards");
            _forwardFailureCounter = _meter.CreateCounter<long>("messages.forward.failure", description: "Failed forwards");
            _idempotencyCreateCounter = _meter.CreateCounter<long>("idempotency.markers.created", description: "Idempotency markers created");
            _idempotencyRemoveCounter = _meter.CreateCounter<long>("idempotency.markers.removed", description: "Idempotency markers removed");

            // Histograms (milliseconds)
            _dbCommandDurationMs = _meter.CreateHistogram<double>("sql.command.duration.ms", unit: "ms", description: "Duration of SQL commands in milliseconds");
            _enqueueLatencyMs = _meter.CreateHistogram<double>("messages.enqueue.latency.ms", unit: "ms", description: "Time to enqueue a message");
            _deliveryLatencyMs = _meter.CreateHistogram<double>("messages.delivery.latency.ms", unit: "ms", description: "Time spent delivering a message (callback)");
            _forwardLatencyMs = _meter.CreateHistogram<double>("messages.forward.latency.ms", unit: "ms", description: "Time spent forwarding a message (callback)");

            // Observable gauge for active connections. Provider can be registered later.
            _activeConnectionsGauge = _meter.CreateObservableGauge<long>(
                "connections.active",
                () => GetActiveConnectionsMeasurement(),
                description: "Number of active connections managed by the connection manager");
        }

        /// <summary>
        /// Register a provider function that returns the current number of active connections.
        /// The provider should be cheap and thread-safe. Passing null unregisters the provider.
        /// </summary>
        public void RegisterActiveConnectionsProvider(Func<int>? provider)
        {
            lock (_providerLock)
            {
                _activeConnectionsProvider = provider;
            }
        }

        private IEnumerable<Measurement<long>> GetActiveConnectionsMeasurement()
        {
            // Read provider reference first to avoid holding try/catch around yield
            var provider = Volatile.Read(ref _activeConnectionsProvider);
            if (provider == null) yield break;

            int value;
            try
            {
                value = provider();
            }
            catch
            {
                // Swallow exceptions from provider to avoid breaking metrics pipeline.
                yield break;
            }

            if (value < 0) value = 0;
            yield return new Measurement<long>(value);
        }

        #region Enqueue metrics

        /// <summary>
        /// Record a single enqueue operation.
        /// </summary>
        public void RecordEnqueue(bool idempotencyHit = false, string? channel = null)
        {
            _enqueueCounter.Add(1, GetCommonTags(channel));
            if (idempotencyHit) _enqueueIdempotentHitCounter.Add(1, GetCommonTags(channel));
        }

        /// <summary>
        /// Record a batch enqueue operation (count = number of messages enqueued).
        /// </summary>
        public void RecordEnqueueBatch(int count, string? channel = null)
        {
            if (count <= 0) return;
            _enqueueBatchCounter.Add(count, GetCommonTags(channel));
        }

        /// <summary>
        /// Record enqueue latency in milliseconds.
        /// </summary>
        public void RecordEnqueueLatency(double milliseconds, string? channel = null)
        {
            if (milliseconds < 0) milliseconds = 0;
            _enqueueLatencyMs.Record(milliseconds, GetCommonTags(channel));
        }

        #endregion

        #region Delivery metrics

        /// <summary>
        /// Record a delivery attempt. Use returned timer to measure latency.
        /// </summary>
        public OperationTimer StartDeliveryAttempt(string? channel = null, string? target = null)
        {
            _deliveryAttemptCounter.Add(1, GetCommonTags(channel, target));
            return new OperationTimer(_deliveryLatencyMs, GetCommonTags(channel, target));
        }

        /// <summary>
        /// Record delivery result.
        /// </summary>
        public void RecordDeliveryResult(bool success, string? channel = null, string? target = null)
        {
            if (success) _deliverySuccessCounter.Add(1, GetCommonTags(channel, target));
            else _deliveryFailureCounter.Add(1, GetCommonTags(channel, target));
        }

        #endregion

        #region Forward metrics

        /// <summary>
        /// Start a forward attempt timer.
        /// </summary>
        public OperationTimer StartForwardAttempt(string? targetInstanceId = null)
        {
            _forwardAttemptCounter.Add(1, GetCommonTags(null, targetInstanceId));
            return new OperationTimer(_forwardLatencyMs, GetCommonTags(null, targetInstanceId));
        }

        /// <summary>
        /// Record forward result.
        /// </summary>
        public void RecordForwardResult(bool success, string? targetInstanceId = null)
        {
            if (success) _forwardSuccessCounter.Add(1, GetCommonTags(null, targetInstanceId));
            else _forwardFailureCounter.Add(1, GetCommonTags(null, targetInstanceId));
        }

        #endregion

        #region Idempotency metrics

        /// <summary>
        /// Record creation of an idempotency marker.
        /// </summary>
        public void RecordIdempotencyCreated(string? key = null)
        {
            _idempotencyCreateCounter.Add(1, GetCommonTags(key));
        }

        /// <summary>
        /// Record removal of an idempotency marker.
        /// </summary>
        public void RecordIdempotencyRemoved(string? key = null)
        {
            _idempotencyRemoveCounter.Add(1, GetCommonTags(key));
        }

        #endregion

        #region DB command metrics

        /// <summary>
        /// Record a database command duration in milliseconds.
        /// </summary>
        public void RecordDbCommandDuration(double milliseconds, string? operation = null, string? table = null)
        {
            if (milliseconds < 0) milliseconds = 0;
            _dbCommandDurationMs.Record(milliseconds, GetCommonTags(operation, table));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Build common tags for metrics. Accepts up to two tag values which are mapped to semantic keys.
        /// </summary>
        private KeyValuePair<string, object?>[] GetCommonTags(string? a, string? b = null)
        {
            // We keep tags small and predictable to avoid cardinality explosion.
            var tags = new List<KeyValuePair<string, object?>>(2);
            if (!string.IsNullOrWhiteSpace(a)) tags.Add(new KeyValuePair<string, object?>("tag", a));
            if (!string.IsNullOrWhiteSpace(b)) tags.Add(new KeyValuePair<string, object?>("target", b));
            return tags.ToArray();
        }

        /// <summary>
        /// Dispose meter resources. After disposal the instance should not be used.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _meter.Dispose();
            }
            catch
            {
                // ignore disposal errors
            }
        }

        #endregion

        #region Nested types

        /// <summary>
        /// Small helper that records elapsed time into a histogram when disposed.
        /// Typical usage:
        /// using(var t = metrics.StartDeliveryAttempt(...)) { await Deliver(...); }
        /// </summary>
        public readonly struct OperationTimer : IDisposable
        {
            private readonly Histogram<double>? _histogram;
            private readonly KeyValuePair<string, object?>[]? _tags;
            private readonly long _startTicks;

            internal OperationTimer(Histogram<double> histogram, KeyValuePair<string, object?>[] tags)
            {
                _histogram = histogram;
                _tags = tags;
                _startTicks = Stopwatch.GetTimestamp();
            }

            /// <summary>
            /// Stop timer and record elapsed milliseconds. Safe to call multiple times.
            /// </summary>
            public void Dispose()
            {
                if (_histogram == null) return;
                var elapsedMs = GetElapsedMilliseconds(_startTicks, Stopwatch.GetTimestamp());
                _histogram.Record(elapsedMs, _tags ?? Array.Empty<KeyValuePair<string, object?>>());
            }

            private static double GetElapsedMilliseconds(long start, long end)
            {
                var delta = end - start;
                return (double)delta * 1000 / Stopwatch.Frequency;
            }
        }

        #endregion
    }
}
