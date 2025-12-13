// src/Infrastructure/Instances/SqlInstanceRegistry.cs
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
    /// SQL Server backed implementation of IPatchInstanceRegistry.
    ///
    /// Expectations about the database:
    ///  - A table (default name: Instances) exists with at least these columns:
    ///      InstanceId (nvarchar(200) PRIMARY KEY),
    ///      Hostname (nvarchar(200) NULL),
    ///      StartedAtUtcTicks (bigint NOT NULL),
    ///      LastHeartbeatUtcTicks (bigint NOT NULL),
    ///      OwnerInstanceId (nvarchar(200) NULL),
    ///      MetadataJson (nvarchar(max) NULL)
    ///
    /// This implementation uses simple ADO.NET (Microsoft.Data.SqlClient) and
    /// conservative transactions for safe concurrent updates.
    /// </summary>
    public sealed class SqlInstanceRegistry : IPatchInstanceRegistry
    {
        private readonly string _connectionString;
        private readonly string _tableName;
        private bool _disposed;

        public SqlInstanceRegistry(string connectionString, string tableName = "Instances")
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _tableName = string.IsNullOrWhiteSpace(tableName) ? "Instances" : tableName;
        }

        /// <summary>
        /// Register or update an instance entry. Returns the stored InstanceInfo.
        /// </summary>
        public async Task<IPatchInstanceRegistry.InstanceInfo> RegisterAsync(
            string instanceId,
            string? hostname = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var nowTicks = DateTime.UtcNow.Ticks;
            var metadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata);

            // Use MERGE to upsert atomically
            var sql =
                $@"MERGE INTO {_tableName} WITH (HOLDLOCK) AS target
                USING (VALUES (@instanceId)) AS src(InstanceId)
                    ON target.InstanceId = src.InstanceId
                WHEN MATCHED THEN
                    UPDATE SET Hostname = @hostname, LastHeartbeatUtcTicks = @nowTicks, -- keep StartedAtUtcTicks as-is
                               MetadataJson = @metadataJson
                WHEN NOT MATCHED THEN
                    INSERT (InstanceId, Hostname, StartedAtUtcTicks, LastHeartbeatUtcTicks, OwnerInstanceId, MetadataJson)
                    VALUES (@instanceId, @hostname, @nowTicks, @nowTicks, NULL, @metadataJson)
                OUTPUT inserted.InstanceId, inserted.Hostname, inserted.StartedAtUtcTicks, inserted.LastHeartbeatUtcTicks, inserted.OwnerInstanceId, inserted.MetadataJson;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@instanceId", SqlDbType.NVarChar, 200) { Value = instanceId });
            cmd.Parameters.Add(new SqlParameter("@hostname", SqlDbType.NVarChar, 200) { Value = (object?)hostname ?? DBNull.Value });
            cmd.Parameters.Add(new SqlParameter("@nowTicks", SqlDbType.BigInt) { Value = nowTicks });
            cmd.Parameters.Add(new SqlParameter("@metadataJson", SqlDbType.NVarChar, -1) { Value = (object?)metadataJson ?? DBNull.Value });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var info = ReadInstanceInfoFromReader(reader);
                await conn.CloseAsync().ConfigureAwait(false);
                return info;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            // Should not reach here, but return a constructed InstanceInfo as fallback
            return new IPatchInstanceRegistry.InstanceInfo
            {
                InstanceId = instanceId,
                Hostname = hostname,
                StartedAtUtcTicks = nowTicks,
                LastHeartbeatUtcTicks = nowTicks,
                Metadata = metadata
            };
        }

        /// <summary>
        /// Send a heartbeat for the given instance id. Returns updated InstanceInfo or null if not found.
        /// </summary>
        public async Task<IPatchInstanceRegistry.InstanceInfo?> HeartbeatAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var nowTicks = DateTime.UtcNow.Ticks;

            var sql =
                $@"UPDATE {_tableName}
                   SET LastHeartbeatUtcTicks = @nowTicks
                 WHERE InstanceId = @instanceId;
                 SELECT InstanceId, Hostname, StartedAtUtcTicks, LastHeartbeatUtcTicks, OwnerInstanceId, MetadataJson
                   FROM {_tableName}
                  WHERE InstanceId = @instanceId;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@nowTicks", SqlDbType.BigInt) { Value = nowTicks });
            cmd.Parameters.Add(new SqlParameter("@instanceId", SqlDbType.NVarChar, 200) { Value = instanceId });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var info = ReadInstanceInfoFromReader(reader);
                await conn.CloseAsync().ConfigureAwait(false);
                return info;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// Try to get the InstanceInfo for the given instance id.
        /// </summary>
        public async Task<IPatchInstanceRegistry.InstanceInfo?> TryGetAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var sql =
                $@"SELECT InstanceId, Hostname, StartedAtUtcTicks, LastHeartbeatUtcTicks, OwnerInstanceId, MetadataJson
                   FROM {_tableName}
                  WHERE InstanceId = @instanceId;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@instanceId", SqlDbType.NVarChar, 200) { Value = instanceId });

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var info = ReadInstanceInfoFromReader(reader);
                await conn.CloseAsync().ConfigureAwait(false);
                return info;
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return null;
        }

        /// <summary>
        /// List all known instances. Optionally include stale entries.
        /// </summary>
        public async Task<IReadOnlyList<IPatchInstanceRegistry.InstanceInfo>> ListInstancesAsync(bool includeStale = false, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            string sql;
            if (includeStale)
            {
                sql =
                $@"SELECT InstanceId, Hostname, StartedAtUtcTicks, LastHeartbeatUtcTicks, OwnerInstanceId, MetadataJson
                   FROM {_tableName};";
            }
            else
            {
                // Consider stale as older than 5 minutes by default if caller didn't want stale; but since caller controls includeStale,
                // we will simply return entries where LastHeartbeatUtcTicks is recent relative to now - caller can pass includeStale=true to override.
                // Here we choose a reasonable default threshold of 5 minutes.
                var cutoffTicks = DateTime.UtcNow.AddMinutes(-5).Ticks;
                sql =
                    $@"SELECT InstanceId, Hostname, StartedAtUtcTicks, LastHeartbeatUtcTicks, OwnerInstanceId, MetadataJson
                       FROM {_tableName}
                      WHERE LastHeartbeatUtcTicks >= @cutoff;";
                await using var connTmp = new SqlConnection(_connectionString);
                await connTmp.OpenAsync(ct).ConfigureAwait(false);
                await using var cmdTmp = new SqlCommand(sql, connTmp);
                cmdTmp.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.BigInt) { Value = cutoffTicks });

                var listTmp = new List<IPatchInstanceRegistry.InstanceInfo>();
                await using var readerTmp = await cmdTmp.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await readerTmp.ReadAsync(ct).ConfigureAwait(false))
                {
                    listTmp.Add(ReadInstanceInfoFromReader(readerTmp));
                }

                await connTmp.CloseAsync().ConfigureAwait(false);
                return listTmp;
            }

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var list = new List<IPatchInstanceRegistry.InstanceInfo>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                list.Add(ReadInstanceInfoFromReader(reader));
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return list;
        }

        /// <summary>
        /// Attempt to claim the target instance on behalf of ownerInstanceId.
        /// If expectedOwnerInstanceId is provided, the claim will only succeed when the current owner matches it.
        /// </summary>
        public async Task<bool> TryClaimAsync(string targetInstanceId, string ownerInstanceId, string? expectedOwnerInstanceId = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (string.IsNullOrWhiteSpace(ownerInstanceId)) throw new ArgumentNullException(nameof(ownerInstanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            string whereClause;
            if (expectedOwnerInstanceId == null)
            {
                // Only claim if currently unclaimed (OwnerInstanceId IS NULL)
                whereClause = "OwnerInstanceId IS NULL";
            }
            else
            {
                // Claim only if current owner equals expectedOwnerInstanceId (can be NULL string)
                whereClause = "(OwnerInstanceId = @expectedOwner OR (OwnerInstanceId IS NULL AND @expectedOwner IS NULL))";
            }

            var sql =
                $@"UPDATE {_tableName}
                   SET OwnerInstanceId = @owner
                 WHERE InstanceId = @target AND {whereClause};";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@owner", SqlDbType.NVarChar, 200) { Value = ownerInstanceId });
            cmd.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 200) { Value = targetInstanceId });
            cmd.Parameters.Add(new SqlParameter("@expectedOwner", SqlDbType.NVarChar, 200) { Value = (object?)expectedOwnerInstanceId ?? DBNull.Value });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Release a previously acquired claim on the target instance.
        /// Only succeeds if the current owner matches ownerInstanceId.
        /// </summary>
        public async Task<bool> ReleaseClaimAsync(string targetInstanceId, string ownerInstanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(targetInstanceId)) throw new ArgumentNullException(nameof(targetInstanceId));
            if (string.IsNullOrWhiteSpace(ownerInstanceId)) throw new ArgumentNullException(nameof(ownerInstanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var sql =
                $@"UPDATE {_tableName}
                   SET OwnerInstanceId = NULL
                 WHERE InstanceId = @target AND OwnerInstanceId = @owner;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@target", SqlDbType.NVarChar, 200) { Value = targetInstanceId });
            cmd.Parameters.Add(new SqlParameter("@owner", SqlDbType.NVarChar, 200) { Value = ownerInstanceId });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Remove an instance entry from the registry.
        /// </summary>
        public async Task<bool> RemoveAsync(string instanceId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentNullException(nameof(instanceId));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var sql =
                $@"DELETE FROM {_tableName}
                  WHERE InstanceId = @instanceId;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@instanceId", SqlDbType.NVarChar, 200) { Value = instanceId });

            var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            await conn.CloseAsync().ConfigureAwait(false);
            return rows > 0;
        }

        /// <summary>
        /// Remove entries considered stale according to the provided threshold.
        /// Returns the list of instance ids that were removed.
        /// </summary>
        public async Task<IReadOnlyList<string>> RemoveStaleAsync(TimeSpan staleThreshold, CancellationToken ct = default)
        {
            if (staleThreshold <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleThreshold));
            if (_disposed) throw new ObjectDisposedException(nameof(SqlInstanceRegistry));

            var cutoffTicks = DateTime.UtcNow.Add(-staleThreshold).Ticks;

            // Select ids to remove, then delete them in a single statement returning deleted ids (OUTPUT clause)
            var sql =
                $@"DELETE FROM {_tableName}
                OUTPUT deleted.InstanceId
                 WHERE LastHeartbeatUtcTicks < @cutoff;";

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add(new SqlParameter("@cutoff", SqlDbType.BigInt) { Value = cutoffTicks });

            var removed = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                removed.Add(reader.GetString(0));
            }

            await conn.CloseAsync().ConfigureAwait(false);
            return removed;
        }

        /// <summary>
        /// Optional helper to compute a canonical instance id. Here we simply return the provided id.
        /// </summary>
        public string MakeInstanceId(string rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId)) throw new ArgumentNullException(nameof(rawId));
            return rawId;
        }

        private static IPatchInstanceRegistry.InstanceInfo ReadInstanceInfoFromReader(SqlDataReader reader)
        {
            var instanceId = reader.GetString(reader.GetOrdinal("InstanceId"));
            var hostname = reader.IsDBNull(reader.GetOrdinal("Hostname")) ? null : reader.GetString(reader.GetOrdinal("Hostname"));
            var startedAt = reader.GetInt64(reader.GetOrdinal("StartedAtUtcTicks"));
            var lastHb = reader.GetInt64(reader.GetOrdinal("LastHeartbeatUtcTicks"));
            var owner = reader.IsDBNull(reader.GetOrdinal("OwnerInstanceId")) ? null : reader.GetString(reader.GetOrdinal("OwnerInstanceId"));
            var metadataJson = reader.IsDBNull(reader.GetOrdinal("MetadataJson")) ? null : reader.GetString(reader.GetOrdinal("MetadataJson"));

            IReadOnlyDictionary<string, string>? metadata = null;
            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                try
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
                    metadata = dict;
                }
                catch
                {
                    metadata = null;
                }
            }

            return new IPatchInstanceRegistry.InstanceInfo
            {
                InstanceId = instanceId,
                Hostname = hostname,
                StartedAtUtcTicks = startedAt,
                LastHeartbeatUtcTicks = lastHb,
                OwnerInstanceId = owner,
                Metadata = metadata
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
