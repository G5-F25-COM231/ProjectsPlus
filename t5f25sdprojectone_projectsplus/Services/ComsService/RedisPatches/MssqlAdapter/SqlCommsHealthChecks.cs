// src/Infrastructure/Health/SqlCommsHealthChecks.cs
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    /// <summary>
    /// Health checks for SQL-backed comms components.
    ///
    /// Provides:
    ///  - a lightweight connectivity check that opens a connection and runs a trivial query
    ///  - a table existence / basic-read check that attempts to SELECT TOP 1 from a configured table
    ///
    /// Register via the AddSqlCommsHealthChecks extension methods below.
    /// </summary>
    public static class SqlCommsHealthChecks
    {
        /// <summary>
        /// Add health checks for the SQL comms subsystem using SqlCommsOptions resolved from DI.
        /// Registers:
        ///  - "sql_comms_database" connectivity check
        ///  - "sql_comms_messages_table" read check (MessagesTable)
        ///  - "sql_comms_deadletters_table" read check (DeadLettersTable)
        ///  - "sql_comms_instances_table" read check (InstancesTable)
        ///  - "sql_comms_connections_table" read check (ConnectionsTable)
        ///  - "sql_comms_idempotency_table" read check (IdempotencyTable)
        /// </summary>
        public static IHealthChecksBuilder AddSqlCommsHealthChecks(this IHealthChecksBuilder builder, IServiceProvider services)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (services == null) throw new ArgumentNullException(nameof(services));

            var opts = services.GetService<SqlCommsOptions>() ?? throw new InvalidOperationException("SqlCommsOptions must be registered in the service provider before adding health checks.");

            // Database connectivity
            builder.Add(new HealthCheckRegistration(
                "sql_comms_database",
                sp => new SqlDatabaseHealthCheck(opts.ConnectionString),
                failureStatus: null,
                tags: new[] { "sql", "comms" }));

            // Table checks (best-effort; each performs a trivial SELECT TOP 1)
            builder.Add(new HealthCheckRegistration(
                "sql_comms_messages_table",
                sp => new SqlTableHealthCheck(opts.ConnectionString, opts.MessagesTable),
                failureStatus: null,
                tags: new[] { "sql", "comms", "messages" }));

            builder.Add(new HealthCheckRegistration(
                "sql_comms_deadletters_table",
                sp => new SqlTableHealthCheck(opts.ConnectionString, opts.DeadLettersTable),
                failureStatus: null,
                tags: new[] { "sql", "comms", "deadletters" }));

            builder.Add(new HealthCheckRegistration(
                "sql_comms_instances_table",
                sp => new SqlTableHealthCheck(opts.ConnectionString, opts.InstancesTable),
                failureStatus: null,
                tags: new[] { "sql", "comms", "instances" }));

            builder.Add(new HealthCheckRegistration(
                "sql_comms_connections_table",
                sp => new SqlTableHealthCheck(opts.ConnectionString, opts.ConnectionsTable),
                failureStatus: null,
                tags: new[] { "sql", "comms", "connections" }));

            builder.Add(new HealthCheckRegistration(
                "sql_comms_idempotency_table",
                sp => new SqlTableHealthCheck(opts.ConnectionString, opts.IdempotencyTable),
                failureStatus: null,
                tags: new[] { "sql", "comms", "idempotency" }));

            return builder;
        }

        /// <summary>
        /// Add a single connectivity health check using an explicit connection string.
        /// </summary>
        public static IHealthChecksBuilder AddSqlCommsDatabaseCheck(this IHealthChecksBuilder builder, string connectionString, string name = "sql_comms_database")
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));

            builder.Add(new HealthCheckRegistration(
                name,
                sp => new SqlDatabaseHealthCheck(connectionString),
                failureStatus: null,
                tags: new[] { "sql", "comms" }));

            return builder;
        }

        /// <summary>
        /// Add a table read check for a specific table name.
        /// </summary>
        public static IHealthChecksBuilder AddSqlCommsTableCheck(this IHealthChecksBuilder builder, string connectionString, string tableName, string name = null)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            if (string.IsNullOrWhiteSpace(tableName)) throw new ArgumentNullException(nameof(tableName));

            var checkName = string.IsNullOrWhiteSpace(name) ? $"sql_table_{tableName}" : name;
            builder.Add(new HealthCheckRegistration(
                checkName,
                sp => new SqlTableHealthCheck(connectionString, tableName),
                failureStatus: null,
                tags: new[] { "sql", "comms", "table" }));

            return builder;
        }

        /// <summary>
        /// Lightweight health check that verifies a SQL Server connection can be opened and a trivial query executed.
        /// </summary>
        private sealed class SqlDatabaseHealthCheck : IHealthCheck
        {
            private readonly string _connectionString;

            public SqlDatabaseHealthCheck(string connectionString)
            {
                _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            }

            public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            {
                try
                {
                    await using var conn = new SqlConnection(_connectionString);
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                    // Run a trivial query that is cheap and safe
                    await using var cmd = new SqlCommand("SELECT 1", conn);
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 5; // short timeout for health check

                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    await conn.CloseAsync().ConfigureAwait(false);

                    if (scalar is int i && i == 1)
                        return HealthCheckResult.Healthy("SQL connection OK");

                    return HealthCheckResult.Degraded("SQL connection opened but test query returned unexpected result");
                }
                catch (Exception ex)
                {
                    return HealthCheckResult.Unhealthy("SQL connectivity check failed", ex);
                }
            }
        }

        /// <summary>
        /// Health check that verifies a table exists and is readable by performing SELECT TOP 1 1 FROM [table].
        /// This is intentionally non-invasive and uses a short command timeout.
        /// </summary>
        private sealed class SqlTableHealthCheck : IHealthCheck
        {
            private readonly string _connectionString;
            private readonly string _tableName;

            public SqlTableHealthCheck(string connectionString, string tableName)
            {
                _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
                _tableName = tableName ?? throw new ArgumentNullException(nameof(tableName));
            }

            public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
            {
                try
                {
                    await using var conn = new SqlConnection(_connectionString);
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

                    // Use QUOTENAME to avoid SQL injection via table name; if tableName contains schema, split and quote both parts.
                    var quoted = QuoteTableName(_tableName);

                    var sql = $"SELECT TOP (1) 1 FROM {quoted};";

                    await using var cmd = new SqlCommand(sql, conn);
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 5;

                    var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                    await conn.CloseAsync().ConfigureAwait(false);

                    if (scalar is int i && i == 1)
                        return HealthCheckResult.Healthy($"Table '{_tableName}' is readable");

                    return HealthCheckResult.Degraded($"Table '{_tableName}' read returned unexpected result");
                }
                catch (SqlException sqlEx)
                {
                    // Common case: table does not exist or permission denied
                    return HealthCheckResult.Unhealthy($"Table '{_tableName}' check failed", sqlEx);
                }
                catch (Exception ex)
                {
                    return HealthCheckResult.Unhealthy($"Table '{_tableName}' check failed", ex);
                }
            }

            /// <summary>
            /// Safely quote a table name that may include a schema (e.g., "dbo.Messages").
            /// Uses simple QUOTENAME-style quoting to avoid injection.
            /// </summary>
            private static string QuoteTableName(string tableName)
            {
                // Split on dot to support schema-qualified names
                var parts = tableName.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < parts.Length; i++)
                {
                    var p = parts[i].Trim();
                    // Replace any closing bracket to avoid breaking quoting
                    p = p.Replace("]", "]]");
                    parts[i] = $"[{p}]";
                }

                return string.Join(".", parts);
            }
        }
    }
}
