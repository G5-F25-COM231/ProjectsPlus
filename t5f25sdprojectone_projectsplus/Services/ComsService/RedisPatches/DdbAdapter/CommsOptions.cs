// src/Infrastructure/Comms/CommsOptions.cs
using System;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Configuration options for the comms infrastructure.
    /// Bound from configuration (e.g., "Comms" section).
    /// </summary>
    public sealed class CommsOptions
    {
        /// <summary>
        /// DynamoDB table name used by the DDB adapter (required).
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// Logical id of this instance (used for ownership/forwarding).
        /// If not provided, callers should generate one and register it with the instance registry.
        /// </summary>
        public string? InstanceId { get; set; }

        /// <summary>
        /// Base address (public) for this instance used when forwarding messages to this instance.
        /// Example: "https://my-host:5001"
        /// </summary>
        public string? InstanceAddress { get; set; }

        /// <summary>
        /// Relative path on remote instances to forward messages to.
        /// Default: "/internal/forward/message"
        /// </summary>
        public string ForwardPath { get; set; } = "/internal/forward/message";

        /// <summary>
        /// Poll delay used by background workers when no work is found.
        /// </summary>
        public TimeSpan PollDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Page size used when scanning for queued messages.
        /// </summary>
        public int DeliveryPageSize { get; set; } = 25;

        /// <summary>
        /// Message retention in minutes (used to compute TTL for message and idempotency markers).
        /// Default: 7 days.
        /// </summary>
        public int MessageRetentionMinutes { get; set; } = 60 * 24 * 7;

        /// <summary>
        /// Maximum delivery attempts before marking a message as failed.
        /// </summary>
        public int MaxDeliveryAttempts { get; set; } = 5;

        /// <summary>
        /// Optional HTTP client name to use for remote forwarding (IHttpClientFactory).
        /// If null or empty, a default HttpClient should be used.
        /// </summary>
        public string? ForwarderHttpClientName { get; set; }

        /// <summary>
        /// Delivery worker poll interval in milliseconds (optional).
        /// </summary>
        public int DeliveryPollMs { get; set; } = 500;
    }
}
