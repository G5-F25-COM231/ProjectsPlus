// src/Infrastructure/Idempotency/SqlIdempotencyStore.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Simple SQL-backed idempotency marker store.
    ///
    /// Expectations about the database:
    ///  - A table (default name: IdempotencyMarkers) exists with at least these columns:
    ///      Key (nvarchar(200) PRIMARY KEY),
    ///      MessageId (nvarchar(200) NOT NULL),
    ///      CreatedAtUtcTicks (bigint NOT NULL),
    ///      TtlUnixSeconds (bigint NULL)
    ///
    /// Typical usage:
    ///  - TryCreateMarkerAsync attempts to create a marker for an idempotency key and returns true when created.
    ///    If a marker already exists the existing MessageId is returned via out parameter.
    ///  - TryGetMessageIdAsync returns the message id associated with a key if present and not expired.
    ///  - RemoveMarkerAsync deletes a marker (cleanup after processing).
    ///  - CleanupExpiredAsync removes expired markers (optional maintenance).
    ///
    /// This implementation is intentionally small and uses straightforward ADO.NET calls.
    /// It tolerates races by relying on a UNIQUE/PRIMARY KEY constraint on Key and handling unique-constraint exceptions.
    /// </summary>
    public sealed class SqlIdempotencyStore : IIdempotencyStore, IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        /// <summary>
        /// Create a new store.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="tableName">Table name to persist markers (default: IdempotencyMarkers).</param>
        public SqlIdempotencyStore(string connectionString, string tableName = "IdempotencyMarkers")
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tableName = string.IsNullOrWhiteSpace(tableName) ? "IdempotencyMarkers" : tableName;
        }

        /// <summary>
        /// Try to get the message id associated with the given idempotency key.
        /// Returns null when not found or expired.
        /// </summary>
        public async Task<string?> TryGetMessageIdAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlIdempotencyStore));

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var sql =
                $@"SELECT MessageId, TtlUnixSeconds
                   FROM {_tableName}
                  WHERE [Key] = @key;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                await conn.CloseAsync().ConfigureAwait(false);
                return null;
            }

            var messageId = reader.GetString(0);
            var ttl = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);

            await conn.CloseAsync().ConfigureAwait(false);

            if (ttl.HasValue && ttl.Value <= nowUnix)
            {
                // expired
                return null;
            }

            return messageId;
        }

        /// <summary>
        /// Try to create an idempotency marker for the given key and messageId.
        /// Returns true when the marker was created. If a marker already exists, returns false and sets existingMessageId.
        /// If retention is provided, a TTL (unix seconds) will be stored.
        /// </summary>
        public async Task<(bool Created, string? ExistingMessageId)> TryCreateMarkerAsync(
            string key,
            string messageId,
            TimeSpan? retention = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlIdempotencyStore));

            long? ttl = null;
            if (retention.HasValue && retention.Value > TimeSpan.Zero)
                ttl = DateTimeOffset.UtcNow.Add(retention.Value).ToUnixTimeSeconds();

            var createdTicks = DateTime.UtcNow.Ticks;

            var insertSql =
                $@"INSERT INTO {_tableName} ([Key], MessageId, CreatedAtUtcTicks, TtlUnixSeconds)
                  VALUES (@key, @messageId, @createdAt, @ttl);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            try
            {
                await using var cmd = new SqlCommand(insertSql, conn);
                cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });
                cmd.Parameters.Add(new SqlParameter("@messageId", SqlDbType.NVarChar, 200) { Value = messageId });
                cmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });

                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                await conn.CloseAsync().ConfigureAwait(false);
                return (true, null);
            }
            catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Marker already exists; read existing message id
                var selectSql =
                    $@"SELECT MessageId, TtlUnixSeconds
                       FROM {_tableName}
                      WHERE [Key] = @key;";

                await using var selCmd = new SqlCommand(selectSql, conn);
                selCmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });

                await using var reader = await selCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var existingMessageId = reader.GetString(0);
                    var existingTtl = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    await conn.CloseAsync().ConfigureAwait(false);

                    // If existing marker is expired, attempt to replace it (best-effort)
                    if (existingTtl.HasValue && existingTtl.Value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    {
                        // Try to delete expired marker and insert new one
                        try
                        {
                            var deleteSql = $@"DELETE FROM {_tableName} WHERE [Key] = @key AND TtlUnixSeconds <= @nowUnix;";
                            await using var delCmd = new SqlCommand(deleteSql, conn);
                            delCmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });
                            delCmd.Parameters.Add(new SqlParameter("@nowUnix", SqlDbType.BigInt) { Value = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                            var deleted = await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                            if (deleted > 0)
                            {
                                // Try insert again
                                await using var retryCmd = new SqlCommand(insertSql, conn);
                                retryCmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });
                                retryCmd.Parameters.Add(new SqlParameter("@messageId", SqlDbType.NVarChar, 200) { Value = messageId });
                                retryCmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = createdTicks });
                                retryCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)ttl ?? DBNull.Value });

                                await retryCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                                await conn.CloseAsync().ConfigureAwait(false);
                                return (true, null);
                            }
                        }
                        catch
                        {
                            // ignore and fall through to returning existing id
                        }
                    }

                    return (false, existingMessageId);
                }

                await conn.CloseAsync().ConfigureAwait(false);
                return (false, null);
            }
            finally
            {
                try { await conn.CloseAsync().ConfigureAwait(false); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Remove an idempotency marker by key. Returns true when a row was deleted.
        /// </summary>
        public async Task<bool> RemoveMarkerAsync(string key, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlIdempotencyStore));

            var sql = $@"DELETE FROM {_tableName} WHERE [Key] = @key;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@key", SqlDbType.NVarChar, 200) { Value = key });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Remove expired markers. Returns the number of removed rows.
        /// </summary>
        public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlIdempotencyStore));

            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var sql = $@"DELETE FROM {_tableName} WHERE TtlUnixSeconds IS NOT NULL AND TtlUnixSeconds <= @nowUnix;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@nowUnix", SqlDbType.BigInt) { Value = nowUnix });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows;
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
