// src/Infrastructure/Enqueue/SqlMessageEnqueuer.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// SQL Server backed implementation of IPatchMessageEnqueuer.
    /// 
    /// Expectations about the database:
    ///  - A table (default name: Messages) exists with at least these columns:
    ///      Id (nvarchar(200) PRIMARY KEY),
    ///      Channel (nvarchar(200) NULL),
    ///      MessageId (nvarchar(200) NOT NULL),
    ///      Payload (nvarchar(max) NOT NULL),
    ///      IdempotencyKey (nvarchar(200) NULL UNIQUE), -- optional but recommended
    ///      CreatedAtUtcTicks (bigint NOT NULL),
    ///      Attempts (int NOT NULL),
    ///      TtlUnixSeconds (bigint NULL),
    ///      Status (nvarchar(100) NULL)
    ///  - A UNIQUE constraint on IdempotencyKey is recommended to avoid races.
    /// 
    /// This implementation uses simple ADO.NET (Microsoft.Data.SqlClient) and
    /// conservative transactions to provide idempotency semantics.
    /// </summary>
    public sealed class SqlMessageEnqueuer : IPatchMessageEnqueuer
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        /// <summary>
        /// Create a new enqueuer.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="tableName">Table name to write messages into (default: Messages).</param>
        public SqlMessageEnqueuer(string connectionString, string tableName = "Messages")
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tableName = string.IsNullOrWhiteSpace(tableName) ? "Messages" : tableName;
        }

        /// <summary>
        /// Enqueue a single message. Honors idempotencyKey when provided.
        /// Returns the canonical message id (MakeMessageId(channel, messageId)).
        /// </summary>
        public async Task<string> EnqueueAsync(
            string? channel,
            string? messageId,
            string payload,
            string? idempotencyKey = null,
            TimeSpan? retention = null,
            CancellationToken ct = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlMessageEnqueuer));

            // Ensure messageId
            var msgId = string.IsNullOrWhiteSpace(messageId) ? Guid.NewGuid().ToString("N") : messageId;
            var canonicalId = MakeMessageId(channel, msgId);
            var createdTicks = DateTime.UtcNow.Ticks;
            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            // If idempotencyKey provided, try to return existing id first, otherwise insert.
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Use a transaction to reduce race window. If a unique constraint exists on IdempotencyKey,
            // we catch the unique violation and select the existing row.
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    // Try to find existing message with this idempotency key
                    var selectSql = $"SELECT Id FROM {_tableName} WHERE IdempotencyKey = @idempotencyKey";
                    await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                    {
                        selCmd.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = idempotencyKey });
                        var existing = await selCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                        if (existing != null && existing != DBNull.Value)
                        {
                            await tx.CommitAsync(ct).ConfigureAwait(false);
                            return (string)existing;
                        }
                    }
                }

                // Insert new message
                var insertSql =
                    $@"INSERT INTO {_tableName}
                        (Id, Channel, MessageId, Payload, IdempotencyKey, CreatedAtUtcTicks, Attempts, TtlUnixSeconds, Status)
                      VALUES
                        (@id, @channel, @messageId, @payload, @idempotencyKey, @createdAt, @attempts, @ttl, @status);";

                await using (var insCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx))
                {
                    insCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = canonicalId });
                    insCmd.Parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 200) { Value = (object?)channel ?? DBNull.Value });
                    insCmd.Parameters.Add(new SqlParameter("@messageId", SqlDbType.NVarChar, 200) { Value = msgId });
                    insCmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = payload });
                    insCmd.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = (object?)idempotencyKey ?? DBNull.Value });
                    insCmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                    insCmd.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = 0 });
                    insCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });
                    insCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = DBNull.Value });

                    try
                    {
                        await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        return canonicalId;
                    }
                    catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
                    {
                        // Unique constraint violation likely on IdempotencyKey (race). Select existing id.
                        // Fall through to select existing row outside catch.
                    }
                }

                // If we reach here due to unique constraint, select the existing id by idempotency key
                if (!string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    var selectSql2 = $"SELECT Id FROM {_tableName} WHERE IdempotencyKey = @idempotencyKey";
                    await using (var selCmd2 = new SqlCommand(selectSql2, conn, (SqlTransaction)tx))
                    {
                        selCmd2.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = idempotencyKey });
                        var existing2 = await selCmd2.ExecuteScalarAsync(ct).ConfigureAwait(false);
                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        if (existing2 != null && existing2 != DBNull.Value)
                            return (string)existing2;
                    }
                }

                // As a last resort, commit and return the canonical id (insert may have succeeded but exception handling prevented detection)
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return canonicalId;
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
        /// Enqueue a batch of messages. This implementation attempts to insert all messages in a single transaction.
        /// For simplicity it uses the same logic as EnqueueAsync but in a single DB transaction to reduce round-trips.
        /// Returns list of canonical ids in the same order as input.
        /// </summary>
        public async Task<IReadOnlyList<string>> EnqueueBatchAsync(
            IEnumerable<(string? Channel, string? MessageId, string Payload, string? IdempotencyKey, TimeSpan? Retention)> messages,
            CancellationToken ct = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlMessageEnqueuer));

            var results = new List<string>();

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                foreach (var msg in messages)
                {
                    ct.ThrowIfCancellationRequested();

                    var msgId = string.IsNullOrWhiteSpace(msg.MessageId) ? Guid.NewGuid().ToString("N") : msg.MessageId;
                    var canonicalId = MakeMessageId(msg.Channel, msgId);
                    var createdTicks = DateTime.UtcNow.Ticks;
                    long? ttl = null;
                    if (msg.Retention.HasValue && msg.Retention.Value > TimeSpan.Zero)
                        ttl = DateTimeOffset.UtcNow.Add(msg.Retention.Value).ToUnixTimeSeconds();

                    if (!string.IsNullOrWhiteSpace(msg.IdempotencyKey))
                    {
                        // Try existing
                        var selectSql = $"SELECT Id FROM {_tableName} WHERE IdempotencyKey = @idempotencyKey";
                        await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                        {
                            selCmd.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = msg.IdempotencyKey });
                            var existing = await selCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                            if (existing != null && existing != DBNull.Value)
                            {
                                results.Add((string)existing);
                                continue;
                            }
                        }
                    }

                    // Insert
                    var insertSql =
                        $@"INSERT INTO {_tableName}
                            (Id, Channel, MessageId, Payload, IdempotencyKey, CreatedAtUtcTicks, Attempts, TtlUnixSeconds, Status)
                          VALUES
                            (@id, @channel, @messageId, @payload, @idempotencyKey, @createdAt, @attempts, @ttl, @status);";

                    await using (var insCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx))
                    {
                        insCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = canonicalId });
                        insCmd.Parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 200) { Value = (object?)msg.Channel ?? DBNull.Value });
                        insCmd.Parameters.Add(new SqlParameter("@messageId", SqlDbType.NVarChar, 200) { Value = msgId });
                        insCmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = msg.Payload });
                        insCmd.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = (object?)msg.IdempotencyKey ?? DBNull.Value });
                        insCmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                        insCmd.Parameters.Add(new SqlParameter("@attempts", SqlDbType.Int) { Value = 0 });
                        insCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });
                        insCmd.Parameters.Add(new SqlParameter("@status", SqlDbType.NVarChar, 100) { Value = DBNull.Value });

                        try
                        {
                            await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                            results.Add(canonicalId);
                        }
                        catch (SqlException ex) when (IsUniqueConstraintViolation(ex) && !string.IsNullOrWhiteSpace(msg.IdempotencyKey))
                        {
                            // Race on idempotency key: select existing
                            var selectSql2 = $"SELECT Id FROM {_tableName} WHERE IdempotencyKey = @idempotencyKey";
                            await using (var selCmd2 = new SqlCommand(selectSql2, conn, (SqlTransaction)tx))
                            {
                                selCmd2.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = msg.IdempotencyKey });
                                var existing2 = await selCmd2.ExecuteScalarAsync(ct).ConfigureAwait(false);
                                if (existing2 != null && existing2 != DBNull.Value)
                                {
                                    results.Add((string)existing2);
                                }
                                else
                                {
                                    // If we cannot find it, rethrow
                                    throw;
                                }
                            }
                        }
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
        /// Remove an idempotency marker. Returns true when a row was updated (IdempotencyKey cleared).
        /// This is optional and depends on whether the table stores the idempotency key in the message row.
        /// </summary>
        public async Task<bool> RemoveIdempotencyMarkerAsync(string idempotencyKey, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentNullException(nameof(idempotencyKey));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlMessageEnqueuer));

            var sql =
            $@"UPDATE {_tableName}
               SET IdempotencyKey = NULL
             WHERE IdempotencyKey = @idempotencyKey;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@idempotencyKey", SqlDbType.NVarChar, 200) { Value = idempotencyKey });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Build canonical message id.
        /// </summary>
        public string MakeMessageId(string? channel, string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (string.IsNullOrWhiteSpace(channel)) return $"msg:{messageId}";
            return $"msg:{channel}:{messageId}";
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
