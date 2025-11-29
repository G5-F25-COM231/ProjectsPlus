using System.Text;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;


namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureDDB
    {
        private readonly IAmazonDynamoDB _ddb;
        private readonly Infralogger _logger;
        private readonly string _region;

        private EnsureDdbRequest? _lastRequest;
        private EnsureDdbResult? _lastResult;
        private readonly object _stateLock = new();

        private const string EnsureIdentifier = "EnsureDDB";
        private const string ResourceTypeName = "DynamoDBTable";

        public EnsureDDB(IAmazonDynamoDB ddb, Infralogger logger, string region)
        {
            _ddb = ddb ?? throw new ArgumentNullException(nameof(ddb));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // EnsureCreateAsync: idempotent create with Tags
        public async Task<EnsureDdbResult> EnsureCreateAsync(EnsureDdbRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var canonical = EnsureUtils.buildCanonicalName(req.BaseName);
            var tableName = canonical;
            lock (_stateLock) { _lastRequest = req; _lastResult = null; }

            if (await DescribeTableExistsAsync(tableName, ct).ConfigureAwait(false))
            {
                var arn = await GetTableArnAsync(tableName, ct).ConfigureAwait(false);
                var already = new EnsureDdbResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    TableName = tableName,
                    TableArn = arn,
                    Message = "Table already exists",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = already; }
                Console.WriteLine($"[EnsureDDB] Already exists: {tableName}");
                return already;
            }

            var createReq = new CreateTableRequest
            {
                TableName = tableName,
                AttributeDefinitions = req.AttributeDefinitions.Select(a => new AttributeDefinition { AttributeName = a.AttributeName, AttributeType = a.AttributeType }).ToList(),
                KeySchema = req.KeySchema.Select(k => new KeySchemaElement(k.AttributeName, k.KeyType)).ToList()
            };

            if (req.UseOnDemand)
            {
                createReq.BillingMode = BillingMode.PAY_PER_REQUEST;
            }
            else
            {
                createReq.ProvisionedThroughput = new ProvisionedThroughput { ReadCapacityUnits = req.ReadCapacity, WriteCapacityUnits = req.WriteCapacity };
            }

            // Add canonical tag so resources are consistently tagged
            createReq.Tags = new List<Amazon.DynamoDBv2.Model.Tag>
            {
                new Amazon.DynamoDBv2.Model.Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix }
            };

            CreateTableResponse createResp;
            try
            {
                createResp = await _ddb.CreateTableAsync(createReq, ct).ConfigureAwait(false);
            }
            catch (ResourceInUseException)
            {
                var arn = await GetTableArnAsync(tableName, ct).ConfigureAwait(false);
                var raced = new EnsureDdbResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    TableName = tableName,
                    TableArn = arn,
                    Message = "Table created concurrently by another actor",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = raced; }
                Console.WriteLine($"[EnsureDDB] Race detected; treating as existing: {tableName}");
                return raced;
            }

            var active = await WaitForTableActiveAsync(tableName, ct).ConfigureAwait(false);
            if (!active)
            {
                var failed = new EnsureDdbResult { Created = false, AlreadyExisted = false, TableName = tableName, TableArn = null, Message = "Timed out waiting for ACTIVE", LoggedRecords = Array.Empty<ResourceRecord>() };
                lock (_stateLock) { _lastResult = failed; }
                Console.WriteLine($"[EnsureDDB] Creation did not reach ACTIVE state: {tableName}");
                return failed;
            }

            var tableArn = await GetTableArnAsync(tableName, ct).ConfigureAwait(false);

            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, tableName, tableArn ?? tableName, _region);
            try
            {
                await _logger.appendAsync(rec).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureDDB] Warning: failed to append log for {tableName}: {ex.Message}");
            }

            var success = new EnsureDdbResult
            {
                Created = true,
                AlreadyExisted = false,
                TableName = tableName,
                TableArn = tableArn,
                Message = "Table created",
                LoggedRecords = new[] { rec }
            };
            lock (_stateLock) { _lastResult = success; }
            Console.WriteLine($"[EnsureDDB] Created table: {tableName} (arn: {tableArn})");
            return success;
        }

        // EnsureExistsAsync: filter by Name/Id when provided; otherwise return all recorded DDB entries
        public async Task<EnsureExistsSummary> EnsureExistsAsync(string? tableNameOrArn = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll() ?? Array.Empty<ResourceRecord>();
            var ddbRecords = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(tableNameOrArn))
            {
                var key = tableNameOrArn.Trim();
                ddbRecords = ddbRecords.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var entries = new List<EnsureExistsEntry>();
            foreach (var rec in ddbRecords)
            {
                ct.ThrowIfCancellationRequested();
                var exists = await DescribeTableExistsAsync(rec.Name, ct).ConfigureAwait(false);
                entries.Add(new EnsureExistsEntry { TableName = rec.Name, TableArn = rec.Id, LoggedAt = rec.CreatedAt, ExistsInCloud = exists });
            }

            var summary = new EnsureExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count(e => e.ExistsInCloud), Missing = entries.Count(e => !e.ExistsInCloud) };
            return summary;
        }

        // EnsureDestroyAsync: delete table and remove only its log line(s)
        public async Task<EnsureDestroyResult> EnsureDestroyAsync(string tableNameOrArn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(tableNameOrArn)) throw new ArgumentNullException(nameof(tableNameOrArn));
            ct.ThrowIfCancellationRequested();

            var all = _logger.readAll() ?? Array.Empty<ResourceRecord>();
            var matches = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)
                                      && (string.Equals(r.Name, tableNameOrArn, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, tableNameOrArn, StringComparison.OrdinalIgnoreCase)))
                             .ToList();

            if (matches.Count == 0)
            {
                if (!await DescribeTableExistsAsync(tableNameOrArn, ct).ConfigureAwait(false))
                {
                    return new EnsureDestroyResult { Destroyed = false, NotFound = true, TableName = tableNameOrArn, Message = "No log entry and table not found", RemovedRecords = Array.Empty<ResourceRecord>() };
                }

                matches.Add(new ResourceRecord { EnsureIdentifier = EnsureIdentifier, ResourceType = ResourceTypeName, Name = tableNameOrArn, Id = tableNameOrArn, Region = _region, CreatedAt = DateTime.UtcNow });
            }

            var removed = new List<ResourceRecord>();
            var anyDeleted = false;
            foreach (var rec in matches)
            {
                ct.ThrowIfCancellationRequested();
                var exists = await DescribeTableExistsAsync(rec.Name, ct).ConfigureAwait(false);
                if (!exists)
                {
                    // remove only log lines that match this EnsureIdentifier and the exact Name/Id
                    TryRemoveLogRecord(rec);
                    continue;
                }

                try
                {
                    await _ddb.DeleteTableAsync(new DeleteTableRequest { TableName = rec.Name }, ct).ConfigureAwait(false);
                    await WaitForTableDeletedAsync(rec.Name, ct).ConfigureAwait(false);
                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    anyDeleted = true;
                    Console.WriteLine($"[EnsureDDB] Deleted table: {rec.Name}");
                }
                catch (ResourceNotFoundException)
                {
                    TryRemoveLogRecord(rec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureDDB] Error deleting {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureDestroyResult { Destroyed = anyDeleted, NotFound = removed.Count == 0, TableName = tableNameOrArn, Message = anyDeleted ? "Deleted and removed log entries" : "No table deleted", RemovedRecords = removed };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("EnsureDDB Snapshot:");
                if (_lastRequest != null)
                {
                    sb.AppendLine($" BaseName={EnsureUtils.normalizeName(_lastRequest.BaseName)}");
                    sb.AppendLine($" UseOnDemand={_lastRequest.UseOnDemand}");
                    sb.AppendLine($" ReadCapacity={_lastRequest.ReadCapacity} WriteCapacity={_lastRequest.WriteCapacity}");
                    if (_lastRequest.AttributeDefinitions != null) sb.AppendLine($" Attrs={string.Join(',', _lastRequest.AttributeDefinitions.Select(a => a.AttributeName + ':' + a.AttributeType))}");
                    if (_lastRequest.KeySchema != null) sb.AppendLine($" Keys={string.Join(',', _lastRequest.KeySchema.Select(k => k.AttributeName + ':' + k.KeyType))}");
                }
                else sb.AppendLine(" Request=null");

                if (_lastResult != null)
                {
                    sb.AppendLine($" Created={_lastResult.Created}");
                    sb.AppendLine($" AlreadyExisted={_lastResult.AlreadyExisted}");
                    sb.AppendLine($" TableName={_lastResult.TableName}");
                    sb.AppendLine($" TableArn={_lastResult.TableArn}");
                }
                else sb.AppendLine(" Result=null");

                return sb.ToString();
            }
        }

        #region internal helpers

        private async Task<bool> DescribeTableExistsAsync(string tableName, CancellationToken ct)
        {
            try
            {
                var resp = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                return resp?.Table != null && string.Equals(resp.Table.TableStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            }
            catch (ResourceNotFoundException) { return false; }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureDDB] DescribeTable error for {tableName}: {ex.Message}");
                return false;
            }
        }

        private async Task<string?> GetTableArnAsync(string tableName, CancellationToken ct)
        {
            try
            {
                var resp = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                return resp?.Table?.TableArn;
            }
            catch { return null; }
        }

        private async Task<bool> WaitForTableActiveAsync(string tableName, CancellationToken ct)
        {
            const int maxAttempts = 30;
            const int delayMs = 1000;
            for (int i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var resp = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                    if (resp?.Table != null && string.Equals(resp.Table.TableStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch (ResourceNotFoundException) { }
                catch { /* ignore transient */ }

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            return false;
        }

        private async Task WaitForTableDeletedAsync(string tableName, CancellationToken ct)
        {
            const int maxAttempts = 20;
            const int delayMs = 1000;
            for (int i = 0; i < maxAttempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var resp = await _ddb.DescribeTableAsync(new DescribeTableRequest { TableName = tableName }, ct).ConfigureAwait(false);
                    if (resp?.Table == null) return;
                }
                catch (ResourceNotFoundException) { return; }
                catch { /* ignore */ }

                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        // Remove only log lines that match EnsureIdentifier + ResourceType + Name + Id
        private void TryRemoveLogRecord(ResourceRecord rec)
        {
            try
            {
                var lines = _logger.readAll()?.ToList() ?? new List<ResourceRecord>();
                var matches = lines.Where(r =>
                    string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ResourceType, rec.ResourceType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.Id, rec.Id, StringComparison.OrdinalIgnoreCase)).ToList();

                if (matches.Count == 0) return;

                var kept = lines.Except(matches).ToList();
                _logger.clear();
                foreach (var k in kept) _logger.appendAsync(k).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureDDB] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public sealed class EnsureDdbRequest
        {
            public string BaseName { get; init; } = "ddb";
            public int ReadCapacity { get; init; } = 5;
            public int WriteCapacity { get; init; } = 5;
            public bool UseOnDemand { get; init; } = true;
            public IReadOnlyList<AttributeDefinition> AttributeDefinitions { get; init; } = new[]
            {
                new AttributeDefinition("Id", ScalarAttributeType.S)
            };
            public IReadOnlyList<KeySchemaElement> KeySchema { get; init; } = new[]
            {
                new KeySchemaElement("Id", KeyType.HASH)
            };
            public string? CorrelationId { get; init; }
        }

        public sealed class EnsureDdbResult
        {
            public bool Created { get; init; }
            public bool AlreadyExisted { get; init; }
            public string? TableName { get; init; }
            public string? TableArn { get; init; }
            public string? Message { get; init; }
            public IReadOnlyList<ResourceRecord> LoggedRecords { get; init; } = Array.Empty<ResourceRecord>();
        }

        public sealed class EnsureExistsEntry
        {
            public string TableName { get; init; } = string.Empty;
            public string? TableArn { get; init; }
            public DateTime LoggedAt { get; init; }
            public bool ExistsInCloud { get; init; }
        }

        public sealed class EnsureExistsSummary
        {
            public IReadOnlyList<EnsureExistsEntry> Entries { get; init; } = Array.Empty<EnsureExistsEntry>();
            public int Total { get; init; }
            public int Found { get; init; }
            public int Missing { get; init; }
        }

        public sealed class EnsureDestroyResult
        {
            public bool Destroyed { get; init; }
            public bool NotFound { get; init; }
            public string? TableName { get; init; }
            public string? Message { get; init; }
            public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
        }

        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureDDB.cs
//
// EnsureDDB (updated)
// - Adds Tags to CreateTableRequest with canonical project tag.
// - Ensures TryRemoveLogRecord only removes log lines produced by EnsureDDB (matches EnsureIdentifier).
// - Otherwise behavior unchanged: idempotent create, best-effort destroy, exists summary, deterministic ToString.
//
// Notes:
// - Requires EnsureUtils and Infralogger from EnsureUtils.cs in the same namespace.
// - DynamoDB CreateTableRequest.Tags uses Amazon.DynamoDBv2.Model.Tag (Key/Value).
// - Tag added: Key = "Project", Value = EnsureUtils.canonicalPrefix
//



// src/IaC_ProjectsPlus/EnsureModules/EnsureDDB.cs
//
// EnsureDDB
// - Ensures a DynamoDB table exists using the canonical naming convention from EnsureUtils.
// - Writes and reads ResourceRecord lines via EnsureUtils' Infralogger (ensureIdentifier = "EnsureDDB").
// - Idempotent Create; best-effort Destroy; read-only Exists that returns summaries.
// - Deterministic ToString snapshot for tests.
//
// Usage:
//   var ensure = new EnsureDDB(ddbClient, new Infralogger(path), region);
//   var req = new EnsureDdbRequest { BaseName = "projects", UseOnDemand = true };
//   var res = await ensure.EnsureCreateAsync(req);
//   var existsSummary = await ensure.EnsureExistsAsync();
//   var del = await ensure.EnsureDestroyAsync(res.TableName);
//
// Notes:
// - This file depends on EnsureUtils (EnsureUtils.buildCanonicalName, EnsureUtils.makeResourceRecord, ResourceRecord, Infralogger).
// - The Infralogger methods use camelCase (appendAsync/readAll/existsAsync/clear) per EnsureUtils convention.
// - Minimal console logging included to aid local testing.