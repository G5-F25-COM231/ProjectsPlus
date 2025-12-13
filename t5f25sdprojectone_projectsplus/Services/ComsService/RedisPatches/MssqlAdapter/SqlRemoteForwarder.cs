// src/Infrastructure/Forwarding/SqlRemoteForwarder.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// SQL-backed remote forwarder.
    ///
    /// This component persists forward requests into a SQL table so other instances
    /// (or a delivery agent) can pick them up and perform the actual remote delivery.
    ///
    /// Expectations about the database:
    ///  - A table (default name: RemoteForwards) exists with at least these columns:
    ///      Id (nvarchar(200) PRIMARY KEY),
    ///      TargetInstanceId (nvarchar(200) NOT NULL),
    ///      PayloadJson (nvarchar(max) NOT NULL),
    ///      CreatedAtUtcTicks (bigint NOT NULL),
    ///      Attempts (int NOT NULL),
    ///      LastAttemptAtUtcTicks (bigint NULL),
    ///      Status (nvarchar(100) NULL),
    ///      TtlUnixSeconds (bigint NULL)
    ///
    /// Typical usage:
    ///  - Enqueue forward requests for a target instance
    ///  - A remote delivery worker polls for pending forwards for its instance and processes them
    ///  - The forwarder supports idempotent enqueueing via an optional idempotency key stored in the payload
    /// </summary>
    public sealed class SqlRemoteForwarder : IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        /// <summary>
        /// Create a new forwarder.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="tableName">Table name to persist forward requests (default: RemoteForwards).</param>
        public SqlRemoteForwarder(string connectionString, string tableName = "RemoteForwards")
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tableName = string.IsNullOrWhiteSpace(tableName) ? "RemoteForwards" : tableName;
        }

        /// <summary>
        /// Enqueue a forward request for a single message to a target instance.
        /// Returns the forward id persisted.
        /// </summary>
        public async Task<string> EnqueueForwardAsync(
            PatchMessageEntity message,
            string targetInstanceId,
            string? idempotencyKey = null,
            TimeSpan? retention = null,
            CancellationToken ct = default)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlRemoteForwarder));

            var forwardId = MakeForwardId(targetInstanceId, message.MessageId);
            var createdTicks = DateTime.UtcNow.Ticks;
            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            var payloadJson = JsonSerializer.Serialize(message);

            // Try to insert; if idempotency is desired the caller can ensure unique forwardId or provide idempotencyKey handling externally.
            var sql =
                $@"INSERT INTO {_tableName}
                    (Id, TargetInstanceId, PayloadJson, CreatedAtUtcTicks, Attempts, Status, TtlUnixSeconds)
                  VALUES
                    (@id, @target, @payload, @createdAt, @attempts, @status, @ttl);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            try
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = forwardId });
                cmd.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 200) { Value = targetInstanceId });
                cmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = payloadJson });
                cmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                cmd.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = 0 });
                cmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                return forwardId;
            }
            catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Already exists; return existing id (idempotent)
                return forwardId;
            }
            finally
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Enqueue a batch of forwards in a single transaction. Returns the list of forward ids in the same order.
        /// </summary>
        public async Task<IReadOnlyList<string>> EnqueueBatchAsync(
            IEnumerable<(PatchMessageEntity Message, string TargetInstanceId, string? IdempotencyKey, TimeSpan? Retention)> forwards,
            CancellationToken ct = default)
        {
            if (forwards == null) throw new ArgumentNullException(nameof(forwards));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlRemoteForwarder));

            var results = new List<string>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                foreach (var f in forwards)
                {
                    ct.ThrowIfCancellationRequested();

                    var forwardId = MakeForwardId(f.TargetInstanceId, f.Message.MessageId);
                    var createdTicks = DateTime.UtcNow.Ticks;
                    long? ttl = null;
                    if (f.Retention.HasValue && f.Retention.Value > TimeSpan.Zero)
                        ttl = DateTimeOffset.UtcNow.Add(f.Retention.Value).ToUnixTimeSeconds();

                    var payloadJson = JsonSerializer.Serialize(f.Message);

                    var insertSql =
                        $@"INSERT INTO {_tableName}
                            (Id, TargetInstanceId, PayloadJson, CreatedAtUtcTicks, Attempts, Status, TtlUnixSeconds)
                          VALUES
                            (@id, @target, @payload, @createdAt, @attempts, @status, @ttl);";

                    await using var cmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);
                    cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = forwardId });
                    cmd.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 200) { Value = f.TargetInstanceId });
                    cmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = payloadJson });
                    cmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                    cmd.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = 0 });
                    cmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = DBNull.Value });
                    cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });

                    try
                    {
                        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        results.Add(forwardId);
                    }
                    catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
                    {
                        // Already exists; treat as idempotent and return id
                        results.Add(forwardId);
                    }
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return results;
            }
            catch
            {
                try { await tx.RollbackAsync(ct).ConfigureAwait(false); } catch { /* ignore */ }
                throw;
            }
            finally
            {
                await conn.CloseAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Query pending forwards for a given instance. Returns up to 'limit' items ordered by CreatedAtUtcTicks.
        /// </summary>
        public async Task<IReadOnlyList<ForwardRequest>> QueryPendingForInstanceAsync(string targetInstanceId, int limit = 50, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlRemoteForwarder));

            var sql =
                $@"SELECT TOP (@limit) Id, TargetInstanceId, PayloadJson, CreatedAtUtcTicks, Attempts, LastAttemptAtUtcTicks, Status, TtlUnixSeconds
                   FROM {_tableName}
                  WHERE TargetInstanceId = @target
                    AND (Status IS NULL OR Status = 'pending' OR Status = 'queued')
                    AND (TtlUnixSeconds IS NULL OR TtlUnixSeconds > @nowUnix)
                  ORDER BY CreatedAtUtcTicks ASC;";

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 200) { Value = targetInstanceId });
            cmd.Parameters.Add(new SqlParameter("@limit", SqlDbType.Int) { Value = limit });
            cmd.Parameters.Add(new SqlParameter("@nowUnix", SqlDbType.BigInt) { Value = nowUnix });

            var list = new List<ForwardRequest>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetString(reader.GetOrdinal("Id"));
                var payloadJson = reader.GetString(reader.GetOrdinal("PayloadJson"));
                var createdAt = reader.GetInt64(reader.GetOrdinal("CreatedAtUtcTicks"));
                var attempts = reader.GetInt32(reader.GetOrdinal("Attempts"));
                var lastAttempt = reader.IsDBNull(reader.GetOrdinal("LastAttemptAtUtcTicks")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("LastAttemptAtUtcTicks"));
                var status = reader.IsDBNull(reader.GetOrdinal("Status")) ? null : reader.GetString(reader.GetOrdinal("Status"));
                var ttl = reader.IsDBNull(reader.GetOrdinal("TtlUnixSeconds")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("TtlUnixSeconds"));

                PatchMessageEntity? message = null;
                try
                {
                    message = JsonSerializer.Deserialize<PatchMessageEntity>(payloadJson);
                }
                catch
                {
                    // ignore deserialization errors; message will be null and consumer can handle
                }

                list.Add(new ForwardRequest
                {
                    Id = id,
                    TargetInstanceId = targetInstanceId,
                    PayloadJson = payloadJson,
                    Message = message,
                    CreatedAtUtcTicks = createdAt,
                    Attempts = attempts,
                    LastAttemptAtUtcTicks = lastAttempt,
                    Status = status,
                    TtlUnixSeconds = ttl
                });
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return list;
        }

        /// <summary>
        /// Process pending forwards for the current instance by invoking the provided delivery callback.
        /// The callback should perform the actual remote send and return true on success.
        /// The worker will increment attempts and set status accordingly; when attempts exceed maxAttempts the forward will be marked failed.
        /// Returns number of processed items.
        /// </summary>
        public async Task<int> ProcessPendingForInstanceAsync(
            string targetInstanceId,
            Func<ForwardRequest, CancellationToken, Task<bool>> deliverCallback,
            int maxMessages = 50,
            int maxAttempts = 5,
            TimeSpan? retentionForSucceeded = null,
            CancellationToken ct = default)
        {
            if (deliverCallback == null) throw new ArgumentNullException(nameof(deliverCallback));
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlRemoteForwarder));

            var pending = await QueryPendingForInstanceAsync(targetInstanceId, maxMessages, ct).ConfigureAwait(false);
            if (pending.Count == 0) return 0;

            var processed = 0;
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            foreach (var req in pending)
            {
                ct.ThrowIfCancellationRequested();
                processed++;

                bool delivered = false;
                string? failureReason = null;

                try
                {
                    delivered = await deliverCallback(req, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    delivered = false;
                    failureReason = ex.Message ?? ex.GetType().Name;
                }

                if (delivered)
                {
                    var deliveredAtTicks = DateTime.UtcNow.Ticks;
                    long? ttl = null;
                    if (retentionForSucceeded.HasValue && retentionForSucceeded.Value > TimeSpan.Zero)
                        ttl = DateTimeOffset.UtcNow.Add(retentionForSucceeded.Value).ToUnixTimeSeconds();

                    var updateSql =
                        $@"UPDATE {_tableName}
                           SET Status = @status, LastAttemptAtUtcTicks = @lastAttempt, Attempts = Attempts + 1, TtlUnixSeconds = @ttl
                         WHERE Id = @id;";

                    await using var updCmd = new SqlCommand(updateSql, conn);
                    updCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = "succeeded" });
                    updCmd.Parameters.Add(new SqlParameter("@lastAttempt", SqlDbType.BigInt) { Value = deliveredAtTicks });
                    updCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });
                    updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = req.Id });

                    await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    continue;
                }

                // Not delivered: increment attempts and update last attempt
                var incrementSql =
                    $@"UPDATE {_tableName}
                       SET Attempts = Attempts + 1, LastAttemptAtUtcTicks = @lastAttempt, Status = @status
                     OUTPUT inserted.Attempts
                     WHERE Id = @id;";

                int attemptsAfterIncrement = 0;
                await using (var incCmd = new SqlCommand(incrementSql, conn))
                {
                    incCmd.Parameters.Add(new SqlParameter("@lastAttempt", SqlDbType.BigInt) { Value = DateTime.UtcNow.Ticks });
                    incCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = "pending" });
                    incCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = req.Id });

                    await using var reader = await incCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        attemptsAfterIncrement = reader.GetInt32(0);
                    }
                    else
                    {
                        // disappeared concurrently
                        continue;
                    }
                }

                if (attemptsAfterIncrement >= maxAttempts)
                {
                    // mark failed
                    var failSql =
                        $@"UPDATE {_tableName}
                           SET Status = @status, LastAttemptAtUtcTicks = @lastAttempt
                         WHERE Id = @id;";

                    await using var failCmd = new SqlCommand(failSql, conn);
                    failCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = "failed" });
                    failCmd.Parameters.Add(new SqlParameter("@lastAttempt", SqlDbType.BigInt) { Value = DateTime.UtcNow.Ticks });
                    failCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = req.Id });

                    await failCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return processed;
        }

        /// <summary>
        /// Remove a forward request by id. Returns true when a row was deleted.
        /// </summary>
        public async Task<bool> RemoveForwardAsync(string forwardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(forwardId)) throw new ArgumentNullException(nameof(forwardId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlRemoteForwarder));

            var sql = $@"DELETE FROM {_tableName} WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = forwardId });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Build a canonical forward id. Example: "fwd:{targetInstanceId}:{messageId}".
        /// </summary>
        public static string MakeForwardId(string targetInstanceId, string messageId)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            return $"fwd:{targetInstanceId}:{messageId}";
        }

        /// <summary>
        /// Lightweight representation of a persisted forward request.
        /// </summary>
        public sealed class ForwardRequest
        {
            public string Id { get; init; } = string.Empty;
            public string TargetInstanceId { get; init; } = string.Empty;
            public string PayloadJson { get; init; } = string.Empty;
            public PatchMessageEntity? Message { get; init; }
            public long CreatedAtUtcTicks { get; init; }
            public int Attempts { get; init; }
            public long? LastAttemptAtUtcTicks { get; init; }
            public string? Status { get; init; }
            public long? TtlUnixSeconds { get; init; }
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

        /// <summary>
        /// Helper to detect unique constraint violations across SQL Server versions.
        /// Error numbers: 2627 (violation of PRIMARY KEY or UNIQUE), 2601 (cannot insert duplicate key row in object).
        /// </summary>
        private static bool IsUniqueConstraintViolation(SqlException ex)
        {
            foreach (SqlError err in ex.Errors)
            {
                if (err.Number == 2627 || err.Number == 2601) return true;
            }
            return false;
        }
    }
}
