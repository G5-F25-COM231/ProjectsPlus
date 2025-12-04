// src/Infrastructure/Telemetry/CommsMetrics.cs
using System;
using System.Diagnostics.Metrics;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Lightweight metrics façade for the comms stack.
    /// Exposes a small set of counters and histograms that are easy to record from the
    /// delivery/enqueue/forwarding code and can be scraped by OpenTelemetry / Prometheus exporters.
    /// 
    /// Usage:
    ///   - Register a single instance as a singleton and call the Record* methods.
    ///   - Optionally provide a queueDepthProvider to publish an observable gauge for queue length.
    /// </summary>
    public sealed class CommsMetrics : IDisposable
    {
        private readonly Meter _meter;

        // Counters
        private readonly Counter<long> _messagesEnqueued;
        private readonly Counter<long> _messagesDelivered;
        private readonly Counter<long> _messagesFailed;
        private readonly Counter<long> _messagesForwarded;

        // Histogram for delivery latency (milliseconds)
        private readonly Histogram<double> _deliveryLatencyMs;

        // Optional observable gauge for queue depth
        private readonly ObservableGauge<long>? _queueDepthGauge;
        private readonly Func<long>? _queueDepthProvider;

        private bool _disposed;

        public CommsMetrics(string meterName = "YourApp.Comms", Func<long>? queueDepthProvider = null)
        {
            if (string.IsNullOrWhiteSpace(meterName)) throw new ArgumentNullException(nameof(meterName));

            _meter = new Meter(meterName, "1.0.0");

            _messagesEnqueued = _meter.CreateCounter<long>("comms_messages_enqueued", description: "Number of messages persisted/enqueued");
            _messagesDelivered = _meter.CreateCounter<long>("comms_messages_delivered", description: "Number of messages successfully delivered");
            _messagesFailed = _meter.CreateCounter<long>("comms_messages_failed", description: "Number of messages that failed delivery");
            _messagesForwarded = _meter.CreateCounter<long>("comms_messages_forwarded", description: "Number of messages forwarded to remote instances");

            _deliveryLatencyMs = _meter.CreateHistogram<double>("comms_delivery_latency_ms", unit: "ms", description: "Delivery latency in milliseconds");

            _queueDepthProvider = queueDepthProvider;
            if (_queueDepthProvider != null)
            {
                // Publish an observable gauge for queue depth
                _queueDepthGauge = _meter.CreateObservableGauge(
                    "comms_queue_depth",
                    () => new[] { new Measurement<long>(_queueDepthProvider()) },
                    description: "Approximate number of queued messages awaiting delivery"
                );
            }
        }

        /// <summary>
        /// Record that a message was enqueued.
        /// </summary>
        public void RecordEnqueued(long count = 1, KeyValuePair<string, object?>[]? tags = null)
        {
            if (_disposed) return;
            if (tags == null) _messagesEnqueued.Add(count);
            else _messagesEnqueued.Add(count, ConvertTags(tags));
        }

        /// <summary>
        /// Record a successful delivery.
        /// </summary>
        public void RecordDelivered(long count = 1, KeyValuePair<string, object?>[]? tags = null)
        {
            if (_disposed) return;
            if (tags == null) _messagesDelivered.Add(count);
            else _messagesDelivered.Add(count, ConvertTags(tags));
        }

        /// <summary>
        /// Record a failed delivery.
        /// </summary>
        public void RecordFailed(long count = 1, KeyValuePair<string, object?>[]? tags = null)
        {
            if (_disposed) return;
            if (tags == null) _messagesFailed.Add(count);
            else _messagesFailed.Add(count, ConvertTags(tags));
        }

        /// <summary>
        /// Record that a message was forwarded to another instance.
        /// </summary>
        public void RecordForwarded(long count = 1, KeyValuePair<string, object?>[]? tags = null)
        {
            if (_disposed) return;
            if (tags == null) _messagesForwarded.Add(count);
            else _messagesForwarded.Add(count, ConvertTags(tags));
        }

        /// <summary>
        /// Record delivery latency in milliseconds.
        /// </summary>
        public void RecordDeliveryLatency(double milliseconds, KeyValuePair<string, object?>[]? tags = null)
        {
            if (_disposed) return;
            if (tags == null) _deliveryLatencyMs.Record(milliseconds);
            else _deliveryLatencyMs.Record(milliseconds, ConvertTags(tags));
        }

        /// <summary>
        /// Helper to convert simple tag array to KeyValuePair<string, object?>[] expected by Meter APIs.
        /// This method exists to keep call sites compact; callers may also call the underlying instruments directly.
        /// </summary>
        private static KeyValuePair<string, object?>[] ConvertTags(KeyValuePair<string, object?>[] tags) => tags;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _meter?.Dispose();
        }
    }
}
