// src/Infrastructure/Delivery/SqlDeliveryWorker.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// SQL-backed delivery worker that polls a Messages table, attempts delivery via a provided
    /// delivery callback, updates attempts/status, and moves messages to a dead-letter table
    /// when they exceed the configured retry limit.
    ///
    /// Expectations about the Messages table (same shape used by SqlMessageEnqueuer):
    ///  - Id (nvarchar(200) PRIMARY KEY)
    ///  - Channel (nvarchar(200) NULL)
    ///  - MessageId (nvarchar(200) NOT NULL)
    ///  - Payload (nvarchar(max) NOT NULL)
    ///  - IdempotencyKey (nvarchar(200) NULL)
    ///  - CreatedAtUtcTicks (bigint NOT NULL)
    ///  - Attempts (int NOT NULL)
    ///  - TtlUnixSeconds (bigint NULL)
    ///  - Status (nvarchar(100) NULL)
    ///
    /// Expectations about the DeadLetters table (optional):
    ///  - Id (nvarchar(200) PRIMARY KEY)
    ///  - OriginalMessageId (nvarchar(200) NOT NULL)
    ///  - Channel (nvarchar(200) NULL)
    ///  - Payload (nvarchar(max) NOT NULL)
    ///  - Reason (nvarchar(max) NULL)
    ///  - FailedAtUtcTicks (bigint NOT NULL)
    ///  - Attempts (int NOT NULL)
    ///  - CreatedAtUtcTicks (bigint NOT NULL)
    ///  - TtlUnixSeconds (bigint NULL)
    ///  - MetadataJson (nvarchar(max) NULL)
    ///
    /// This worker is intentionally minimal and focuses on correctness and clear transactional
    /// semantics: it selects a batch of candidate messages, attempts delivery one-by-one,
    /// updates Attempts/Status, and moves to dead-letter when necessary.
    /// </summary>
    public sealed class SqlDeliveryWorker : IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _messagesTable;
        private readonly string _deadLettersTable;
        private readonly Func<PatchMessageEntity, CancellationToken, Task<bool>> _deliverCallback;
        private readonly Action<string>? _logger;
        private bool _disposed;

        /// <summary>
        /// Create a new delivery worker.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="deliverCallback">Callback invoked to deliver a message. Should return true on success.</param>
        /// <param name="messagesTable">Messages table name (default: Messages).</param>
        /// <param name="deadLettersTable">Dead letters table name (default: DeadLetters).</param>
        /// <param name="logger">Optional logger action for diagnostics.</param>
        public SqlDeliveryWorker(
            string connectionString,
            Func<PatchMessageEntity, CancellationToken, Task<bool>> deliverCallback,
            string messagesTable = "Messages",
            string deadLettersTable = "DeadLetters",
            Action<string>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _deliverCallback = deliverCallback ?? throw new ArgumentNullException(nameof(deliverCallback));
            _messagesTable = string.IsNullOrWhiteSpace(messagesTable) ? "Messages" : messagesTable;
            _deadLettersTable = string.IsNullOrWhiteSpace(deadLettersTable) ? "DeadLetters" : deadLettersTable;
            _logger = logger;
        }

        /// <summary>
        /// Process up to <paramref name="maxMessages"/> pending messages once.
        /// Candidate messages are those with Status IS NULL or 'queued' and Attempts &lt;= maxAttempts.
        /// For each message:
        ///  - attempt delivery via _deliverCallback
        ///  - on success: mark Status='delivered', set DeliveredAtUtcTicks and optionally ttl
        ///  - on failure: increment Attempts; if Attempts &gt;= maxAttempts move to dead-letter table and delete from messages table
        /// Returns the number of messages processed.
        /// </summary>
        public async Task<int> ProcessPendingOnceAsync(
            int maxMessages,
            int maxAttempts,
            TimeSpan? retentionForDelivered = null,
            CancellationToken ct = default)
        {
            if (maxMessages <= 0) throw new ArgumentOutOfRangeException(nameof(maxMessages));
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlDeliveryWorker));

            var processed = 0;

            // Select candidate messages ordered by CreatedAtUtcTicks ascending (oldest first)
            var selectSql =
                $@"SELECT TOP (@limit)
                        Id, Channel, MessageId, Payload, TargetType = NULL, TargetId = NULL, CreatedAtUtcTicks, DeliveredAtUtcTicks = NULL, FailedAtUtcTicks = NULL, Attempts, TtlUnixSeconds, Status
                   FROM {_messagesTable}
                  WHERE (Status IS NULL OR Status = 'queued')
                    AND (TtlUnixSeconds IS NULL OR TtlUnixSeconds > @nowUnix)
                    AND Attempts < @maxAttempts
                  ORDER BY CreatedAtUtcTicks ASC;";

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Read candidate messages into memory first to avoid long-running transactions while delivering.
            var candidates = new List<PatchMessageEntity>();
            await using (var cmd = new SqlCommand(selectSql, conn))
            {
                cmd.Parameters.Add(new SqlParameter("@limit", SqlDbType.Int) { Value = maxMessages });
                cmd.Parameters.Add(new SqlParameter("@nowUnix", SqlDbType.BigInt) { Value = nowUnix });
                cmd.Parameters.Add(new SqlParameter("@maxAttempts", SqlDbType.Int) { Value = maxAttempts });

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    candidates.Add(ReadMessageFromReader(reader));
                }
            }

            foreach (var msg in candidates)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                bool delivered = false;
                string? failureReason = null;

                try
                {
                    delivered = await _deliverCallback(msg, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    delivered = false;
                    failureReason = ex.Message ?? ex.GetType().Name;
                    _logger?.Invoke($"Delivery callback threw for message {msg.Id}: {failureReason}");
                }

                if (delivered)
                {
                    // Mark delivered: set Status='delivered', DeliveredAtUtcTicks, keep Attempts as-is
                    var deliveredTicks = DateTime.UtcNow.Ticks;
                    long? ttl = null;
                    if (retentionForDelivered.HasValue && retentionForDelivered.Value > TimeSpan.Zero)
                        ttl = DateTimeOffset.UtcNow.Add(retentionForDelivered.Value).ToUnixTimeSeconds();

                    var updateSql =
                        $@"UPDATE {_messagesTable}
                           SET Status = @status, DeliveredAtUtcTicks = @deliveredAt, TtlUnixSeconds = @ttl
                         WHERE Id = @id;";

                    await using var updCmd = new SqlCommand(updateSql, conn);
                    updCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = "delivered" });
                    updCmd.Parameters.Add(new SqlParameter("@deliveredAt", SqlDbType.BigInt) { Value = deliveredTicks });
                    updCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });
                    updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = msg.Id });

                    var rows = await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    _logger?.Invoke($"Message {msg.Id} delivered successfully; updated rows={rows}.");
                    continue;
                }

                // Not delivered: increment attempts. If attempts after increment >= maxAttempts -> move to dead-letter.
                var incrementSql =
                    $@"UPDATE {_messagesTable}
                       SET Attempts = Attempts + 1
                     OUTPUT inserted.Attempts
                     WHERE Id = @id;";

                int attemptsAfterIncrement = 0;
                await using (var incCmd = new SqlCommand(incrementSql, conn))
                {
                    incCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = msg.Id });
                    await using var incReader = await incCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await incReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        attemptsAfterIncrement = incReader.GetInt32(0);
                    }
                    else
                    {
                        // message disappeared concurrently; skip
                        _logger?.Invoke($"Message {msg.Id} disappeared while incrementing attempts.");
                        continue;
                    }
                }

                _logger?.Invoke($"Message {msg.Id} delivery failed (attempts now {attemptsAfterIncrement}).");

                if (attemptsAfterIncrement >= maxAttempts)
                {
                    // Move to dead-letter table and delete from messages table in a transaction
                    var failedAtTicks = DateTime.UtcNow.Ticks;
                    var deadId = MakeDeadLetterId(msg.Channel, msg.MessageId);
                    var insertDlSql =
                        $@"INSERT INTO {_deadLettersTable}
                            (Id, OriginalMessageId, Channel, Payload, Reason, FailedAtUtcTicks, Attempts, CreatedAtUtcTicks, TtlUnixSeconds, MetadataJson)
                        VALUES
                            (@dlId, @origId, @channel, @payload, @reason, @failedAt, @attempts, @createdAt, @ttl, @meta);";

                    var deleteSql = $@"DELETE FROM {_messagesTable} WHERE Id = @id;";

                    await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
                    try
                    {
                        // Insert dead-letter
                        await using (var insCmd = new SqlCommand(insertDlSql, conn, (SqlTransaction)tx))
                        {
                            insCmd.Parameters.Add(new SqlParameter("@dlId", SqlDbType.NVarChar, 200) { Value = deadId });
                            insCmd.Parameters.Add(new SqlParameter("@origId", SqlDbType.NVarChar, 200) { Value = msg.MessageId });
                            insCmd.Parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 200) { Value = (object?)msg.Channel ?? DBNull.Value });
                            insCmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = msg.Payload });
                            insCmd.Parameters.Add(new SqlParameter("@reason", SqlDbType.NVarChar, -1) { Value = (object?)failureReason ?? DBNull.Value });
                            insCmd.Parameters.Add(new SqlParameter("@failedAt", SqlDbType.BigInt) { Value = failedAtTicks });
                            insCmd.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = attemptsAfterIncrement });
                            insCmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = DateTime.UtcNow.Ticks });
                            insCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = DBNull.Value });
                            insCmd.Parameters.Add(new SqlParameter("@meta", SqlDbType.NVarChar, -1) { Value = DBNull.Value });

                            await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        // Delete original message
                        await using (var delCmd = new SqlCommand(deleteSql, conn, (SqlTransaction)tx))
                        {
                            delCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = msg.Id });
                            await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        }

                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        _logger?.Invoke($"Message {msg.Id} moved to dead-letter {deadId} after {attemptsAfterIncrement} attempts.");
                    }
                    catch (Exception ex)
                    {
                        try { await tx.RollbackAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
                        _logger?.Invoke($"Failed to move message {msg.Id} to dead-letter: {ex.Message}");
                    }
                }
                else
                {
                    // Optionally set status to 'queued' (already queued) and record last failure reason in Status column
                    var updateFailSql =
                        $@"UPDATE {_messagesTable}
                           SET Status = @status
                         WHERE Id = @id;";

                    await using var updFailCmd = new SqlCommand(updateFailSql, conn);
                    updFailCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = "queued" });
                    updFailCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = msg.Id });

                    await updFailCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return processed;
        }

        /// <summary>
        /// Helper to construct a dead-letter id. Example: "dl:{channel}:{messageId}" or "dl:{messageId}".
        /// </summary>
        private static string MakeDeadLetterId(string? channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(channel)) return $"dl:{messageId}";
            return $"dl:{channel}:{messageId}";
        }

        /// <summary>
        /// Read a PatchMessageEntity from a SqlDataReader row. The SELECT used in this worker
        /// maps some Dynamo-style fields to SQL columns; TargetType/TargetId are left null here.
        /// </summary>
        private static PatchMessageEntity ReadMessageFromReader(SqlDataReader reader)
        {
            var id = reader.GetString(reader.GetOrdinal("Id"));
            var channel = reader.IsDBNull(reader.GetOrdinal("Channel")) ? string.Empty : reader.GetString(reader.GetOrdinal("Channel"));
            var messageId = reader.GetString(reader.GetOrdinal("MessageId"));
            var payload = reader.GetString(reader.GetOrdinal("Payload"));
            var createdAt = reader.GetInt64(reader.GetOrdinal("CreatedAtUtcTicks"));
            var attempts = reader.GetInt32(reader.GetOrdinal("Attempts"));
            var ttl = reader.IsDBNull(reader.GetOrdinal("TtlUnixSeconds")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("TtlUnixSeconds"));
            var status = reader.IsDBNull(reader.GetOrdinal("Status")) ? null : reader.GetString(reader.GetOrdinal("Status"));

            return new PatchMessageEntity
            {
                Id = id,
                Channel = channel,
                MessageId = messageId,
                Payload = payload,
                CreatedAtUtcTicks = createdAt,
                Attempts = attempts,
                TtlUnixSeconds = ttl,
                Status = status
            };
        }

        /// <summary>
        /// Dispose pattern.
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
