// src/Infrastructure/Connections/SqlBackedConnectionManager.cs
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// SQL-backed manager for PatchConnectionEntity instances.
    ///
    /// Expectations about the database:
    ///  - A table (default name: Connections) exists with at least these columns:
    ///      Id (nvarchar(200) PRIMARY KEY),
    ///      ConnectionId (nvarchar(200) NOT NULL),
    ///      UserId (nvarchar(200) NULL),
    ///      OwnerInstance (nvarchar(200) NULL),
    ///      RoomsJson (nvarchar(max) NULL), -- JSON array of strings
    ///      CreatedAtUtcTicks (bigint NOT NULL),
    ///      LastHeartbeatUtcTicks (bigint NULL),
    ///      TtlUnixSeconds (bigint NULL),
    ///      Version (bigint NULL)
    ///
    /// This class provides common operations: get, upsert, heartbeat, add/remove rooms,
    /// claim/release owner, query by user, and delete. It uses simple ADO.NET and
    /// conservative transactions where appropriate.
    /// </summary>
    public sealed class SqlBackedConnectionManager : IDisposable, IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        public SqlBackedConnectionManager(string connectionString, string tableName = "Connections")
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tableName = string.IsNullOrWhiteSpace(tableName) ? "Connections" : tableName;
        }

        /// <summary>
        /// Get a connection entity by id. Returns null if not found.
        /// </summary>
        public async Task<PatchConnectionEntity?> GetAsync(string id, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            var sql =
                $@"SELECT Id, ConnectionId, UserId, OwnerInstance, RoomsJson, CreatedAtUtcTicks, LastHeartbeatUtcTicks, TtlUnixSeconds, Version
                   FROM {_tableName}
                  WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var entity = ReadEntityFromReader(reader);
                await conn.CloseAsync().ConfigureAwait(false);
                return entity;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// Upsert (insert or replace) a connection entity. Returns the stored entity (may include server-assigned fields).
        /// If expectedVersion is provided, the operation will only succeed when the current version matches (optimistic concurrency).
        /// </summary>
        public async Task<PatchConnectionEntity> UpsertAsync(PatchConnectionEntity entity, long? expectedVersion = null, CancellationToken ct = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (string.IsNullOrWhiteSpace(entity.Id)) throw new ArgumentException("Entity must have an Id", nameof(entity));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            // We'll store rooms as JSON array in RoomsJson column.
            var roomsJson = (entity.Rooms ?? Array.Empty<string>()).ToArray();
            var roomsJsonText = roomsJson.Length == 0 ? null : JsonSerializer.Serialize(roomsJson);

            // Use MERGE to upsert and optionally enforce version check.
            // If expectedVersion is provided, we will check it in the WHEN MATCHED clause via a WHERE.
            var mergeWhere = expectedVersion.HasValue ? $"AND target.Version = @expectedVersion" : string.Empty;

            var sql =
                $@"MERGE INTO {_tableName} WITH (HOLDLOCK) AS target
                USING (VALUES (@id)) AS src(Id)
                    ON target.Id = src.Id
                WHEN MATCHED AND (1 = 1 {mergeWhere}) THEN
                    UPDATE SET ConnectionId = @connectionId,
                               UserId = @userId,
                               OwnerInstance = @owner,
                               RoomsJson = @roomsJson,
                               CreatedAtUtcTicks = @createdAt,
                               LastHeartbeatUtcTicks = @lastHeartbeat,
                               TtlUnixSeconds = @ttl,
                               Version = @version
                WHEN NOT MATCHED THEN
                    INSERT (Id, ConnectionId, UserId, OwnerInstance, RoomsJson, CreatedAtUtcTicks, LastHeartbeatUtcTicks, TtlUnixSeconds, Version)
                    VALUES (@id, @connectionId, @userId, @owner, @roomsJson, @createdAt, @lastHeartbeat, @ttl, @version)
                OUTPUT inserted.Id, inserted.ConnectionId, inserted.UserId, inserted.OwnerInstance, inserted.RoomsJson, inserted.CreatedAtUtcTicks, inserted.LastHeartbeatUtcTicks, inserted.TtlUnixSeconds, inserted.Version;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
                cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = entity.Id });
                cmd.Parameters.Add(new SqlParameter("@connectionId", SqlDbType.NVarChar, 200) { Value = entity.ConnectionId });
                cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.NVarChar, 200) { Value = (object?)entity.UserId ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@owner", SqlDbType.NVarChar, 200) { Value = (object?)entity.OwnerInstance ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@roomsJson", SqlDbType.NVarChar, -1) { Value = (object?)roomsJsonText ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@createdAt", SqlDbType.BigInt) { Value = entity.CreatedAtUtcTicks });
                cmd.Parameters.Add(new SqlParameter("@lastHeartbeat", SqlDbType.BigInt) { Value = (object?)entity.LastHeartbeatUtcTicks ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = (object?)entity.TtlUnixSeconds ?? DBNull.Value });
                cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = (object?)entity.Version ?? DBNull.Value });

                if (expectedVersion.HasValue)
                {
                    cmd.Parameters.Add(new SqlParameter("@expectedVersion", SqlDbType.BigInt) { Value = expectedVersion.Value });
                }

                await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    var stored = ReadEntityFromReader(reader);
                    await tx.CommitAsync(ct).ConfigureAwait(false);
                    await conn.CloseAsync().ConfigureAwait(false);
                    return stored;
                }

                // If MERGE didn't return a row (e.g., version mismatch), throw concurrency exception
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw new DBConcurrencyException("Upsert failed due to version mismatch or other concurrency condition.");
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
        /// Update heartbeat timestamp to now and optionally bump version and TTL.
        /// Returns updated entity or null if not found.
        /// </summary>
        public async Task<PatchConnectionEntity?> HeartbeatAsync(string id, long? newVersion = null, long? ttlUnixSeconds = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            var nowTicks = DateTime.UtcNow.Ticks;

            var setParts = new List<string> { "LastHeartbeatUtcTicks = @nowTicks" };
            if (newVersion.HasValue) setParts.Add("Version = @version");
            if (ttlUnixSeconds.HasValue) setParts.Add("TtlUnixSeconds = @ttl");

            var sql =
                $@"UPDATE {_tableName}
                   SET {string.Join(", ", setParts)}
                 OUTPUT inserted.Id, inserted.ConnectionId, inserted.UserId, inserted.OwnerInstance, inserted.RoomsJson, inserted.CreatedAtUtcTicks, inserted.LastHeartbeatUtcTicks, inserted.TtlUnixSeconds, inserted.Version
                 WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@nowTicks", SqlDbType.BigInt) { Value = nowTicks });
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
            if (newVersion.HasValue) cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = newVersion.Value });
            if (ttlUnixSeconds.HasValue) cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = ttlUnixSeconds.Value });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var entity = ReadEntityFromReader(reader);
                await conn.CloseAsync().ConfigureAwait(false);
                return entity;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// Add a room to the rooms collection (idempotent). Optionally bump version and set TTL.
        /// Returns updated entity or null if not found.
        /// </summary>
        public async Task<PatchConnectionEntity?> AddRoomAsync(string id, string room, long? newVersion = null, long? ttlUnixSeconds = null, long? expectedVersion = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            // We'll read-modify-write inside a serializable transaction to ensure idempotency.
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                var selectSql =
                    $@"SELECT RoomsJson, Version
                       FROM {_tableName}
                      WHERE Id = @id;";

                await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                {
                    selCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                    await using var reader = await selCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        await tx.RollbackAsync(ct).ConfigureAwait(false);
                        await conn.CloseAsync().ConfigureAwait(false);
                        return null;
                    }

                    var roomsJson = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var currentVersion = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    await reader.CloseAsync().ConfigureAwait(false);

                    if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
                    {
                        await tx.RollbackAsync(ct).ConfigureAwait(false);
                        throw new DBConcurrencyException("Version mismatch while adding room.");
                    }

                    var rooms = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrWhiteSpace(roomsJson))
                    {
                        try
                        {
                            var arr = JsonSerializer.Deserialize<string[]>(roomsJson) ?? Array.Empty<string>();
                            foreach (var r in arr) rooms.Add(r);
                        }
                        catch
                        {
                            // ignore parse errors and treat as empty
                        }
                    }

                    rooms.Add(room);
                    var newRoomsJson = JsonSerializer.Serialize(rooms.ToArray());

                    var updateSql =
                        $@"UPDATE {_tableName}
                           SET RoomsJson = @roomsJson, Version = @version{(ttlUnixSeconds.HasValue ? ", TtlUnixSeconds = @ttl" : string.Empty)}
                         OUTPUT inserted.Id, inserted.ConnectionId, inserted.UserId, inserted.OwnerInstance, inserted.RoomsJson, inserted.CreatedAtUtcTicks, inserted.LastHeartbeatUtcTicks, inserted.TtlUnixSeconds, inserted.Version
                         WHERE Id = @id;";

                    await using var updCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                    updCmd.Parameters.Add(new SqlParameter("@roomsJson", SqlDbType.NVarChar, -1) { Value = newRoomsJson });
                    updCmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = (object?)newVersion ?? DBNull.Value });
                    if (ttlUnixSeconds.HasValue) updCmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = ttlUnixSeconds.Value });
                    updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });

                    await using var updReader = await updCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await updReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var entity = ReadEntityFromReader(updReader);
                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        await conn.CloseAsync().ConfigureAwait(false);
                        return entity;
                    }

                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    throw new DBConcurrencyException("Failed to update rooms while adding.");
                }
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
        /// Remove a room from the rooms collection (idempotent). Optionally bump version.
        /// Returns updated entity or null if not found.
        /// </summary>
        public async Task<PatchConnectionEntity?> RemoveRoomAsync(string id, string room, long? newVersion = null, long? expectedVersion = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrWhiteSpace(room)) throw new ArgumentNullException(nameof(room));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);

            try
            {
                var selectSql =
                    $@"SELECT RoomsJson, Version
                       FROM {_tableName}
                      WHERE Id = @id;";

                await using (var selCmd = new SqlCommand(selectSql, conn, (SqlTransaction)tx))
                {
                    selCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
                    await using var reader = await selCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        await tx.RollbackAsync(ct).ConfigureAwait(false);
                        await conn.CloseAsync().ConfigureAwait(false);
                        return null;
                    }

                    var roomsJson = reader.IsDBNull(0) ? null : reader.GetString(0);
                    var currentVersion = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    await reader.CloseAsync().ConfigureAwait(false);

                    if (expectedVersion.HasValue && currentVersion != expectedVersion.Value)
                    {
                        await tx.RollbackAsync(ct).ConfigureAwait(false);
                        throw new DBConcurrencyException("Version mismatch while removing room.");
                    }

                    var rooms = new HashSet<string>(StringComparer.Ordinal);
                    if (!string.IsNullOrWhiteSpace(roomsJson))
                    {
                        try
                        {
                            var arr = JsonSerializer.Deserialize<string[]>(roomsJson) ?? Array.Empty<string>();
                            foreach (var r in arr) rooms.Add(r);
                        }
                        catch
                        {
                            // ignore parse errors and treat as empty
                        }
                    }

                    if (!rooms.Remove(room))
                    {
                        // nothing to do; return current entity
                        var current = await GetAsync(id, ct).ConfigureAwait(false);
                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        await conn.CloseAsync().ConfigureAwait(false);
                        return current;
                    }

                    var newRoomsJson = rooms.Count == 0 ? null : JsonSerializer.Serialize(rooms.ToArray());

                    var updateSql =
                        $@"UPDATE {_tableName}
                           SET RoomsJson = @roomsJson, Version = @version
                         OUTPUT inserted.Id, inserted.ConnectionId, inserted.UserId, inserted.OwnerInstance, inserted.RoomsJson, inserted.CreatedAtUtcTicks, inserted.LastHeartbeatUtcTicks, inserted.TtlUnixSeconds, inserted.Version
                         WHERE Id = @id;";

                    await using var updCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                    updCmd.Parameters.Add(new SqlParameter("@roomsJson", SqlDbType.NVarChar, -1) { Value = (object?)newRoomsJson ?? DBNull.Value });
                    updCmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = (object?)newVersion ?? DBNull.Value });
                    updCmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });

                    await using var updReader = await updCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (await updReader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var entity = ReadEntityFromReader(updReader);
                        await tx.CommitAsync(ct).ConfigureAwait(false);
                        await conn.CloseAsync().ConfigureAwait(false);
                        return entity;
                    }

                    await tx.RollbackAsync(ct).ConfigureAwait(false);
                    throw new DBConcurrencyException("Failed to update rooms while removing.");
                }
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
        /// Set or clear the owner instance (claim/release). If ownerInstance is null the owner is cleared.
        /// Optionally bump version and set TTL. If expectedOwnerInstanceId is provided, the claim will only succeed when current owner matches it.
        /// Returns true when the update succeeded.
        /// </summary>
        public async Task<bool> SetOwnerAsync(string id, string? ownerInstance, string? expectedOwnerInstanceId = null, long? newVersion = null, long? ttlUnixSeconds = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            // Build conditional WHERE clause for expected owner
            string ownerCondition = expectedOwnerInstanceId == null
                ? "(OwnerInstance = @expectedOwner OR (OwnerInstance IS NULL AND @expectedOwner IS NULL))"
                : "(OwnerInstance = @expectedOwner OR (OwnerInstance IS NULL AND @expectedOwner IS NULL))";

            // We'll perform an UPDATE with condition in WHERE to ensure atomicity
            var setParts = new List<string>();
            if (ownerInstance != null)
                setParts.Add("OwnerInstance = @owner");
            else
                setParts.Add("OwnerInstance = NULL");

            if (newVersion.HasValue) setParts.Add("Version = @version");
            if (ttlUnixSeconds.HasValue) setParts.Add("TtlUnixSeconds = @ttl");

            var sql =
                $@"UPDATE {_tableName}
                   SET {string.Join(", ", setParts)}
                 WHERE Id = @id AND {ownerCondition};";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
            cmd.Parameters.Add(new SqlParameter("@owner", SqlDbType.NVarChar, 200) { Value = (object?)ownerInstance ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@expectedOwner", SqlDbType.NVarChar, 200) { Value = (object?)expectedOwnerInstanceId ?? DBNull.Value });
            if (newVersion.HasValue) cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = newVersion.Value });
            if (ttlUnixSeconds.HasValue) cmd.Parameters.Add(new SqlParameter("@ttl", SqlDbType.BigInt) { Value = ttlUnixSeconds.Value });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Query connections by user id. Returns up to 'limit' results ordered by CreatedAtUtcTicks.
        /// Requires an index on UserId if table is large.
        /// </summary>
        public async Task<IReadOnlyList<PatchConnectionEntity>> QueryByUserAsync(string userId, int limit = 50, bool ascending = true, CancellationToken ct = default)
        {
            if (userId == null) throw new ArgumentNullException(nameof(userId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            var order = ascending ? "ASC" : "DESC";

            var sql =
                $@"SELECT TOP (@limit) Id, ConnectionId, UserId, OwnerInstance, RoomsJson, CreatedAtUtcTicks, LastHeartbeatUtcTicks, TtlUnixSeconds, Version
                   FROM {_tableName}
                  WHERE UserId = @userId
                  ORDER BY CreatedAtUtcTicks {order};";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@userId", SqlDbType.NVarChar, 200) { Value = userId });
            cmd.Parameters.Add(new SqlParameter("@limit", SqlDbType.Int) { Value = limit });

            var list = new List<PatchConnectionEntity>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(ReadEntityFromReader(reader));
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return list;
        }

        /// <summary>
        /// Delete a connection by id. If expectedVersion is provided, delete will be conditional.
        /// Returns true when a row was deleted.
        /// </summary>
        public async Task<bool> DeleteAsync(string id, long? expectedVersion = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlBackedConnectionManager));

            var sql = expectedVersion.HasValue
                ? $@"DELETE FROM {_tableName} WHERE Id = @id AND Version = @version;"
                : $@"DELETE FROM {_tableName} WHERE Id = @id;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@id", SqlDbType.NVarChar, 200) { Value = id });
            if (expectedVersion.HasValue) cmd.Parameters.Add(new SqlParameter("@version", SqlDbType.BigInt) { Value = expectedVersion.Value });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Helper to read a PatchConnectionEntity from a SqlDataReader row.
        /// </summary>
        private static PatchConnectionEntity ReadEntityFromReader(SqlDataReader reader)
        {
            var id = reader.GetString(reader.GetOrdinal("Id"));
            var connectionId = reader.GetString(reader.GetOrdinal("ConnectionId"));
            var userId = reader.IsDBNull(reader.GetOrdinal("UserId")) ? null : reader.GetString(reader.GetOrdinal("UserId"));
            var owner = reader.IsDBNull(reader.GetOrdinal("OwnerInstance")) ? null : reader.GetString(reader.GetOrdinal("OwnerInstance"));
            var roomsJson = reader.IsDBNull(reader.GetOrdinal("RoomsJson")) ? null : reader.GetString(reader.GetOrdinal("RoomsJson"));
            var createdAt = reader.GetInt64(reader.GetOrdinal("CreatedAtUtcTicks"));
            var lastHb = reader.IsDBNull(reader.GetOrdinal("LastHeartbeatUtcTicks")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("LastHeartbeatUtcTicks"));
            var ttl = reader.IsDBNull(reader.GetOrdinal("TtlUnixSeconds")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("TtlUnixSeconds"));
            var version = reader.IsDBNull(reader.GetOrdinal("Version")) ? (long?)null : reader.GetInt64(reader.GetOrdinal("Version"));

            string[] rooms = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(roomsJson))
            {
                try
                {
                    rooms = JsonSerializer.Deserialize<string[]>(roomsJson) ?? Array.Empty<string>();
                }
                catch
                {
                    rooms = Array.Empty<string>();
                }
            }

            return new PatchConnectionEntity
            {
                Id = id,
                ConnectionId = connectionId,
                UserId = userId,
                OwnerInstance = owner,
                Rooms = rooms,
                CreatedAtUtcTicks = createdAt,
                LastHeartbeatUtcTicks = lastHb,
                TtlUnixSeconds = ttl,
                Version = version
            };
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
