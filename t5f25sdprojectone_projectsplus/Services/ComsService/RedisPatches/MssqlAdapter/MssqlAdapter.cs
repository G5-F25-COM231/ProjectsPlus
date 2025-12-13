// src/Infrastructure/MssqlAdapter.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// EF Core / MSSQL-backed adapter implementing IRedisClient.
    /// This implementation uses the provided ProjectsPlusDbContext for connection information
    /// but performs the hot-path read/write operations using ADO.NET commands against the
    /// same connection string. This avoids relying on specific EF entity shapes (init-only,
    /// readonly properties, differing column names) and is resilient to small model differences.
    /// </summary>
    public sealed class MssqlAdapter : IRedisClient, IDisposable
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<MssqlAdapter>? _logger;
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Action<string>, byte>> _localSubs = new();
        private readonly ConcurrentDictionary<string, DateTime> _channelLastSeen = new();
        private readonly TimeSpan _pollInterval;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pollerTask;
        private bool _disposed;
        private static readonly byte _marker = 0;

        private readonly IServiceProvider _services;
        private readonly string _connectionString;

        public MssqlAdapter(IServiceProvider services, TimeSpan? pollInterval = null)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            using var scope = _services.CreateScope();
            _db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>() ?? throw new ArgumentNullException(nameof(ProjectsPlusDbContext));
            _logger = scope.ServiceProvider.GetService<ILogger<MssqlAdapter>>();
            _connectionString = _db.Database.GetDbConnection().ConnectionString ?? throw new InvalidOperationException("DbContext connection string is not available.");

            _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
            _pollerTask = Task.Run(PollerLoopAsync);
        }

        #region Strings (KV)

        public async Task<bool> StringSetAsync(string key, string value, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));

            // Upsert using ADO.NET to avoid depending on EF entity mutability
            const string updateSql = "UPDATE KeyValues SET Value = @value, UpdatedAtUtc = @updated WHERE Id = @id;";
            const string insertSql = "INSERT INTO KeyValues (Id, Value, UpdatedAtUtc) VALUES (@id, @value, @updated);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using (var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false))
            {
                try
                {
                    await using var updCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                    updCmd.Parameters.Add(new SqlParameter("@value", SqlDbType.NVarChar, -1) { Value = (object?)value ?? DBNull.Value });
                    updCmd.Parameters.Add(new SqlParameter("@updated", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
                    updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = key });

                    var rows = await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    if (rows == 0)
                    {
                        await using var insCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);
                        insCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = key });
                        insCmd.Parameters.Add(new SqlParameter("@value", SqlDbType.NVarChar, -1) { Value = (object?)value ?? DBNull.Value });
                        insCmd.Parameters.Add(new SqlParameter("@updated", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
                        await insCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return true;
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
        }

        public async Task<string?> StringGetAsync(string key, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));

            const string sql = "SELECT Value FROM KeyValues WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = key });

            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return result == DBNull.Value || result == null ? null : (string)result;
        }

        public async Task<bool> KeyDeleteAsync(string key, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (key == null) throw new ArgumentNullException(nameof(key));

            const string sql = "DELETE FROM KeyValues WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = key });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        #endregion

        #region Sets

        public async Task<long> SetAddAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            if (member == null) throw new ArgumentNullException(nameof(member));

            var id = $"set:{setKey}";
            const string selectSql = "SELECT MembersJson FROM Sets WHERE Id = @id;";
            const string insertSql = "INSERT INTO Sets (Id, MembersJson, UpdatedAtUtc) VALUES (@id, @members, @updated);";
            const string updateSql = "UPDATE Sets SET MembersJson = @members, UpdatedAtUtc = @updated WHERE Id = @id;";
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
            try
            {
                string? membersJson = null;
                await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                {
                    selCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                    var scalar = await selCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (scalar != null && scalar != DBNull.Value) membersJson = (string)scalar;
                }

                var members = string.IsNullOrWhiteSpace(membersJson)
                    ? new HashSet<string>()
                    : JsonSerializer.Deserialize<HashSet<string>>(membersJson) ?? new HashSet<string>();

                var added = members.Add(member) ? 1L : 0L;
                if (added == 1)
                {
                    var newJson = JsonSerializer.Serialize(members);
                    await using var cmd = new SqlCommand(membersJson == null ? insertSql : updateSql, conn, (SqlTransaction)tx);
                    cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                    cmd.Parameters.Add(new SqlParameter("@members", SqlDbType.NVarChar, -1) { Value = newJson });
                    cmd.Parameters.Add(new SqlParameter("@updated", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return added;
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

        public async Task<long> SetRemoveAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));
            if (member == null) throw new ArgumentNullException(nameof(member));

            var id = $"set:{setKey}";
            const string selectSql = "SELECT MembersJson FROM Sets WHERE Id = @id;";
            const string deleteSql = "DELETE FROM Sets WHERE Id = @id;";
            const string updateSql = "UPDATE Sets SET MembersJson = @members, UpdatedAtUtc = @updated WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                string? membersJson = null;
                await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                {
                    selCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                    var scalar = await selCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    if (scalar != null && scalar != DBNull.Value) membersJson = (string)scalar;
                }

                if (string.IsNullOrWhiteSpace(membersJson))
                {
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    return 0L;
                }

                var members = JsonSerializer.Deserialize<HashSet<string>>(membersJson) ?? new HashSet<string>();
                var removed = members.Remove(member) ? 1L : 0L;
                if (removed == 1)
                {
                    if (members.Count == 0)
                    {
                        await using var delCmd = new SqlCommand(deleteSql, conn, (SqlTransaction)tx);
                        delCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                        await delCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                    else
                    {
                        var newJson = JsonSerializer.Serialize(members);
                        await using var updCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                        updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                        updCmd.Parameters.Add(new SqlParameter("@members", SqlDbType.NVarChar, -1) { Value = newJson });
                        updCmd.Parameters.Add(new SqlParameter("@updated", SqlDbType.DateTime2) { Value = DateTime.UtcNow });
                        await updCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
                return removed;
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

        public async Task<string[]> SetMembersAsync(string setKey, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (setKey == null) throw new ArgumentNullException(nameof(setKey));

            var id = $"set:{setKey}";
            const string selectSql = "SELECT MembersJson FROM Sets WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(selectSql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });

            var scalar = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);

            if (scalar == null || scalar == DBNull.Value) return Array.Empty<string>();
            var membersJson = (string)scalar;
            var members = JsonSerializer.Deserialize<HashSet<string>>(membersJson) ?? new HashSet<string>();
            return members.ToArray();
        }

        public async Task<bool> SetContainsAsync(string setKey, string member, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            var members = await SetMembersAsync(setKey, ct).ConfigureAwait(false);
            return members.Contains(member);
        }

        #endregion

        #region Pub/Sub

        public async Task<long> PublishAsync(string channel, string message, CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (message == null) throw new ArgumentNullException(nameof(message));

            const string insertSql = "INSERT INTO PubSubMessages (Id, Channel, Payload, CreatedAtUtc) VALUES (@id, @channel, @payload, @created);";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Use a GUID for Id column; the table may define Id as uniqueidentifier or nvarchar.
            var newId = Guid.NewGuid();

            await using var cmd = new SqlCommand(insertSql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.UniqueIdentifier) { Value = newId });
            cmd.Parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 200) { Value = channel });
            cmd.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar, -1) { Value = message });
            cmd.Parameters.Add(new SqlParameter("@created", SqlDbType.DateTime2) { Value = DateTime.UtcNow });

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            // Invoke local subscribers immediately (best-effort)
            if (_localSubs.TryGetValue(channel, out var handlers))
            {
                var arr = handlers.Keys.ToArray();
                foreach (var h in arr)
                {
                    _ = Task.Run(() =>
                    {
                        try { h(message); } catch (Exception ex) { _logger?.LogDebug(ex, "Local subscriber handler threw"); }
                    }, ct);
                }

                // update last seen so poller doesn't re-deliver same message
                _channelLastSeen.AddOrUpdate(channel, DateTime.UtcNow, (_, __) => DateTime.UtcNow);
                await conn.CloseAsync().ConfigureAwait(false);
                return arr.Length;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return 0L;
        }

        public IDisposable Subscribe(string channel, Action<string> handler)
        {
            ThrowIfDisposed();
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var handlers = _localSubs.GetOrAdd(channel, _ => new ConcurrentDictionary<Action<string>, byte>());
            handlers[handler] = _marker;
            _channelLastSeen.TryAdd(channel, DateTime.UtcNow);
            return new Subscription(this, channel, handler);
        }

        private async Task PollerLoopAsync()
        {
            var ct = _cts.Token;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var channels = _localSubs.Keys.ToArray();
                        if (channels.Length > 0)
                        {
                            var tasks = channels.Select(ch => PollChannelAsync(ch, ct)).ToArray();
                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "MssqlAdapter poller loop error");
                    }

                    await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { /* expected on dispose */ }
        }

        private async Task PollChannelAsync(string channel, CancellationToken ct)
        {
            if (!_channelLastSeen.TryGetValue(channel, out var lastSeen)) lastSeen = DateTime.MinValue;

            const string selectSql = @"
            SELECT TOP (100) Id, Payload, CreatedAtUtc
                FROM PubSubMessages
                WHERE Channel = @channel AND CreatedAtUtc > @lastSeen
                ORDER BY CreatedAtUtc ASC;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(selectSql, conn);
            cmd.Parameters.Add(new SqlParameter("@channel", SqlDbType.NVarChar, 200) { Value = channel });
            cmd.Parameters.Add(new SqlParameter("@lastSeen", SqlDbType.DateTime2) { Value = lastSeen });

            var msgs = new List<(string Payload, DateTime CreatedAt)>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var payload = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var created = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
                msgs.Add((payload, created));
            }

            if (msgs.Count == 0)
            {
                await conn.CloseAsync().ConfigureAwait(false);
                return;
            }

            DateTime maxSeen = lastSeen;
            foreach (var m in msgs)
            {
                if (_localSubs.TryGetValue(channel, out var handlers))
                {
                    foreach (var h in handlers.Keys.ToArray())
                    {
                        _ = Task.Run(() =>
                        {
                            try { h(m.Payload); } catch (Exception ex) { _logger?.LogDebug(ex, "Subscriber handler threw"); }
                        }, ct);
                    }
                }

                if (m.CreatedAt > maxSeen) maxSeen = m.CreatedAt;
            }

            _channelLastSeen.AddOrUpdate(channel, maxSeen, (_, __) => maxSeen);
            await conn.CloseAsync().ConfigureAwait(false);
        }

        #endregion

        #region Subscription helper

        private sealed class Subscription : IDisposable
        {
            private readonly MssqlAdapter _parent;
            private readonly string _channel;
            private readonly Action<string> _handler;
            private bool _disposed;

            public Subscription(MssqlAdapter parent, string channel, Action<string> handler)
            {
                _parent = parent; _channel = channel; _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_parent._localSubs.TryGetValue(_channel, out var handlers))
                {
                    handlers.TryRemove(_handler, out _);
                    if (handlers.IsEmpty)
                    {
                        _parent._localSubs.TryRemove(_channel, out _);
                        _parent._channelLastSeen.TryRemove(_channel, out _);
                    }
                }
            }
        }

        #endregion

        #region Lifecycle

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MssqlAdapter));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _cts.Cancel();
                _pollerTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch { /* swallow */ }
            finally
            {
                _cts.Dispose();
                _localSubs.Clear();
                _channelLastSeen.Clear();
            }
        }

        #endregion
    }
}
