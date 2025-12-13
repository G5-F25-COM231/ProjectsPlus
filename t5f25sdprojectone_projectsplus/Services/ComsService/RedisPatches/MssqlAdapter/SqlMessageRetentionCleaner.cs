// src/Infrastructure/Maintenance/SqlMessageRetentionCleaner.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Maintenance helper that removes expired messages and optionally expired dead-letters
    /// from the SQL-backed comms tables.
    ///
    /// Responsibilities:
    ///  - Remove messages whose TTL has elapsed (TtlUnixSeconds <= now)
    ///  - Optionally remove delivered messages older than a retention window
    ///  - Optionally remove dead-letters older than a retention window
    ///
    /// This class is intentionally small and synchronous-call friendly: callers can invoke
    /// CleanOnceAsync periodically (e.g., from a background service or scheduled job).
    /// All operations use short-lived connections and simple SQL statements.
    /// </summary>
    public sealed class SqlMessageRetentionCleaner : IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _messagesTable;
        private readonly string _deadLettersTable;
        private readonly Action<string>? _logger;
        private bool _disposed;

        /// <summary>
        /// Create a new cleaner.
        /// </summary>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="messagesTable">Messages table name (default: Messages).</param>
        /// <param name="deadLettersTable">Dead letters table name (default: DeadLetters).</param>
        /// <param name="logger">Optional logger action for diagnostics.</param>
        public SqlMessageRetentionCleaner(
            string connectionString,
            string messagesTable = "Messages",
            string deadLettersTable = "DeadLetters",
            Action<string>? logger = null)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _messagesTable = string.IsNullOrWhiteSpace(messagesTable) ? "Messages" : messagesTable;
            _deadLettersTable = string.IsNullOrWhiteSpace(deadLettersTable) ? "DeadLetters" : deadLettersTable;
            _logger = logger;
        }

        /// <summary>
        /// Run a single pass of retention cleanup.
        ///
        /// Parameters:
        ///  - removeExpiredByTtl: when true, remove messages whose TtlUnixSeconds is set and <= now.
        ///  - removeDeliveredOlderThan: when provided, remove messages with Status='delivered' and DeliveredAtUtcTicks older than now - retention.
        ///  - removeDeadLettersOlderThan: when provided, remove dead-letters with CreatedAtUtcTicks older than now - retention.
        ///
        /// Returns a summary tuple: (messagesRemoved, deadLettersRemoved).
        /// </summary>
        public async Task<(int MessagesRemoved, int DeadLettersRemoved)> CleanOnceAsync(
            bool removeExpiredByTtl = true,
            TimeSpan? removeDeliveredOlderThan = null,
            TimeSpan? removeDeadLettersOlderThan = null,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlMessageRetentionCleaner));

            var messagesRemoved = 0;
            var deadLettersRemoved = 0;
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nowTicks = DateTime.UtcNow.Ticks;

            // Remove messages by TTL
            if (removeExpiredByTtl)
            {
                var sql =
                    $@"DELETE FROM {_messagesTable}
                     OUTPUT deleted.Id
                     WHERE TtlUnixSeconds IS NOT NULL AND TtlUnixSeconds <= @nowUnix;";

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@nowUnix", SqlDbType.BigInt) { Value = nowUnix });

                try
                {
                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        messagesRemoved++;
                    }
                    _logger?.Invoke($"Removed {messagesRemoved} messages by TTL from {_messagesTable}.");
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error removing messages by TTL: {ex.Message}");
                    throw;
                }
                finally
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            // Remove delivered messages older than retention
            if (removeDeliveredOlderThan.HasValue && removeDeliveredOlderThan.Value > TimeSpan.Zero)
            {
                var cutoffTicks = DateTime.UtcNow.Add(-removeDeliveredOlderThan.Value).Ticks;

                var sql =
                    $@"DELETE FROM {_messagesTable}
                     OUTPUT deleted.Id
                     WHERE Status = 'delivered' AND DeliveredAtUtcTicks IS NOT NULL AND DeliveredAtUtcTicks <= @cutoff;";

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.BigInt) { Value = cutoffTicks });

                try
                {
                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    var removed = 0;
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        removed++;
                    }

                    messagesRemoved += removed;
                    _logger?.Invoke($"Removed {removed} delivered messages older than {removeDeliveredOlderThan.Value} from {_messagesTable}.");
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error removing delivered messages: {ex.Message}");
                    throw;
                }
                finally
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            // Remove dead-letters older than retention
            if (removeDeadLettersOlderThan.HasValue && removeDeadLettersOlderThan.Value > TimeSpan.Zero)
            {
                var cutoffTicks = DateTime.UtcNow.Add(-removeDeadLettersOlderThan.Value).Ticks;

                var sql =
                    $@"DELETE FROM {_deadLettersTable}
                     OUTPUT deleted.Id
                     WHERE CreatedAtUtcTicks IS NOT NULL AND CreatedAtUtcTicks <= @cutoff;";

                await using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync(ct).ConfigureAwait(false);
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.BigInt) { Value = cutoffTicks });

                try
                {
                    await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    var removed = 0;
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        removed++;
                    }

                    deadLettersRemoved += removed;
                    _logger?.Invoke($"Removed {removed} dead-letters older than {removeDeadLettersOlderThan.Value} from {_deadLettersTable}.");
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error removing dead-letters: {ex.Message}");
                    throw;
                }
                finally
                {
                    await conn.CloseAsync().ConfigureAwait(false);
                }
            }

            return (messagesRemoved, deadLettersRemoved);
        }

        /// <summary>
        /// Convenience: remove messages that are expired either by TTL or by delivered retention,
        /// and also remove dead-letters older than the provided retention. This method wraps
        /// CleanOnceAsync with a single call and returns the same tuple.
        /// </summary>
        public Task<(int MessagesRemoved, int DeadLettersRemoved)> CleanOnceWithDefaultsAsync(
            TimeSpan? deliveredRetention = null,
            TimeSpan? deadLetterRetention = null,
            CancellationToken ct = default)
        {
            // By default we remove by TTL and optionally by delivered retention / dead-letter retention
            return CleanOnceAsync(
                removeExpiredByTtl: true,
                removeDeliveredOlderThan: deliveredRetention,
                removeDeadLettersOlderThan: deadLetterRetention,
                ct: ct);
        }

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
