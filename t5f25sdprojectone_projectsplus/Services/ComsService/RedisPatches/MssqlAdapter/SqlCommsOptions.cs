// src/Infrastructure/Options/SqlCommsOptions.cs
using System;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Configuration options for SQL-backed comms infrastructure components.
    /// This class centralizes table names, connection string, polling/timeout settings,
    /// and sensible defaults so callers and DI registrations can share a single source of truth.
    /// </summary>
    public sealed class SqlCommsOptions
    {
        /// <summary>
        /// Default constructor with sensible defaults.
        /// </summary>
        public SqlCommsOptions()
        {
            // Connection / table defaults
            ConnectionString = string.Empty;
            MessagesTable = "Messages";
            DeadLettersTable = "DeadLetters";
            RemoteForwardsTable = "RemoteForwards";
            IdempotencyTable = "IdempotencyMarkers";
            InstancesTable = "Instances";
            ConnectionsTable = "Connections";

            // Delivery / forwarding defaults
            MaxDeliveryBatchSize = 50;
            DeliveryMaxAttempts = 5;
            DeliveryPollInterval = TimeSpan.FromSeconds(5);
            DeliveryCommandTimeout = TimeSpan.FromSeconds(30);

            MaxForwardBatchSize = 50;
            ForwardMaxAttempts = 5;
            ForwardPollInterval = TimeSpan.FromSeconds(5);
            ForwardCommandTimeout = TimeSpan.FromSeconds(30);

            // Instance registry defaults
            InstanceHeartbeatInterval = TimeSpan.FromSeconds(10);
            InstanceStaleThreshold = TimeSpan.FromMinutes(5);

            // Idempotency defaults
            DefaultIdempotencyRetention = TimeSpan.FromHours(1);

            // Connection manager defaults
            ConnectionManagerCommandTimeout = TimeSpan.FromSeconds(30);

            // General behavior
            UseSerializableTransactions = true;
            CommandTimeoutSeconds = 30;
        }

        #region Connection / Table Names

        /// <summary>
        /// SQL Server connection string used by all SQL-backed components.
        /// Must be provided by the host environment.
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Table name for messages (used by enqueuer and delivery worker).
        /// </summary>
        public string MessagesTable { get; set; }

        /// <summary>
        /// Table name for dead letters.
        /// </summary>
        public string DeadLettersTable { get; set; }

        /// <summary>
        /// Table name for remote forward requests.
        /// </summary>
        public string RemoteForwardsTable { get; set; }

        /// <summary>
        /// Table name for idempotency markers.
        /// </summary>
        public string IdempotencyTable { get; set; }

        /// <summary>
        /// Table name for instance registry.
        /// </summary>
        public string InstancesTable { get; set; }

        /// <summary>
        /// Table name for connections.
        /// </summary>
        public string ConnectionsTable { get; set; }

        #endregion

        #region Delivery / Forwarding Settings

        /// <summary>
        /// Maximum number of messages to fetch/process in a single delivery poll.
        /// </summary>
        public int MaxDeliveryBatchSize { get; set; }

        /// <summary>
        /// Maximum attempts before a message is moved to dead-letter.
        /// </summary>
        public int DeliveryMaxAttempts { get; set; }

        /// <summary>
        /// How often the delivery worker polls for pending messages.
        /// </summary>
        public TimeSpan DeliveryPollInterval { get; set; }

        /// <summary>
        /// Command timeout used by delivery-related SQL commands.
        /// </summary>
        public TimeSpan DeliveryCommandTimeout { get; set; }

        /// <summary>
        /// Maximum number of forwards to fetch/process in a single poll.
        /// </summary>
        public int MaxForwardBatchSize { get; set; }

        /// <summary>
        /// Maximum attempts before a forward is marked failed.
        /// </summary>
        public int ForwardMaxAttempts { get; set; }

        /// <summary>
        /// How often the forwarder polls for pending forwards.
        /// </summary>
        public TimeSpan ForwardPollInterval { get; set; }

        /// <summary>
        /// Command timeout used by forward-related SQL commands.
        /// </summary>
        public TimeSpan ForwardCommandTimeout { get; set; }

        #endregion

        #region Instance Registry Settings

        /// <summary>
        /// How often an instance should heartbeat to the registry.
        /// </summary>
        public TimeSpan InstanceHeartbeatInterval { get; set; }

        /// <summary>
        /// Threshold after which an instance is considered stale (used by cleanup).
        /// </summary>
        public TimeSpan InstanceStaleThreshold { get; set; }

        #endregion

        #region Idempotency / Retention

        /// <summary>
        /// Default retention for idempotency markers when not explicitly provided.
        /// </summary>
        public TimeSpan DefaultIdempotencyRetention { get; set; }

        /// <summary>
        /// Default retention for delivered messages (used to set TTL when marking delivered).
        /// Null means no TTL by default.
        /// </summary>
        public TimeSpan? DefaultDeliveredMessageRetention { get; set; }

        /// <summary>
        /// Default retention for forwards that succeeded.
        /// </summary>
        public TimeSpan? DefaultForwardRetention { get; set; }

        #endregion

        #region Connection Manager / General

        /// <summary>
        /// Command timeout used by connection-manager related SQL commands.
        /// </summary>
        public TimeSpan ConnectionManagerCommandTimeout { get; set; }

        /// <summary>
        /// Whether to prefer serializable transactions for multi-statement operations.
        /// When true, components will use IsolationLevel.Serializable where implemented.
        /// </summary>
        public bool UseSerializableTransactions { get; set; }

        /// <summary>
        /// Generic command timeout in seconds used as a fallback for components that accept seconds.
        /// </summary>
        public int CommandTimeoutSeconds { get; set; }

        #endregion

        #region Validation

        /// <summary>
        /// Validate options and throw ArgumentException when required values are missing or invalid.
        /// Call during startup to fail fast on misconfiguration.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new ArgumentException("ConnectionString must be provided.", nameof(ConnectionString));

            if (string.IsNullOrWhiteSpace(MessagesTable))
                throw new ArgumentException("MessagesTable must be provided.", nameof(MessagesTable));

            if (string.IsNullOrWhiteSpace(DeadLettersTable))
                throw new ArgumentException("DeadLettersTable must be provided.", nameof(DeadLettersTable));

            if (string.IsNullOrWhiteSpace(RemoteForwardsTable))
                throw new ArgumentException("RemoteForwardsTable must be provided.", nameof(RemoteForwardsTable));

            if (MaxDeliveryBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(MaxDeliveryBatchSize));
            if (MaxForwardBatchSize <= 0) throw new ArgumentOutOfRangeException(nameof(MaxForwardBatchSize));
            if (DeliveryMaxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(DeliveryMaxAttempts));
            if (ForwardMaxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(ForwardMaxAttempts));
            if (InstanceHeartbeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(InstanceHeartbeatInterval));
            if (InstanceStaleThreshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(InstanceStaleThreshold));
        }

        #endregion
    }
}
