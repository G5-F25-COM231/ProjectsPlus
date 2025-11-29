using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.EC2.Model;
using Amazon.RDS;
using Amazon.RDS.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureRDS_bkp
    {
        private readonly IAmazonRDS _rds;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureRDS";
        private const string ResourceTypeName = "RDSInstance";

        private EnsureRdsRequest? _lastRequest;
        private EnsureRdsResult? _lastResult;
        private readonly object _stateLock = new();

        public EnsureRDS_bkp(IAmazonRDS rds, Infralogger logger, string region)
        {
            _rds = rds ?? throw new ArgumentNullException(nameof(rds));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // EnsureCreateAsync - idempotent create
        public async Task<EnsureRdsResult> EnsureCreateAsync(EnsureRdsRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            lock (_stateLock) { _lastRequest = req; _lastResult = null; }

            var dbInstanceId = EnsureUtils.buildCanonicalName(req.BaseName); // use canonical name as instance identifier

            // Check existing by instance identifier
            var existing = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
            if (existing != null)
            {
                var already = new EnsureRdsResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    DBInstanceIdentifier = dbInstanceId,
                    Endpoint = existing.Endpoint?.Address,
                    Message = "RDS instance already exists",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = already; }
                Console.WriteLine($"[EnsureRDS] Instance already exists: {dbInstanceId}");
                return already;
            }

            // Build create request
            var createReq = new CreateDBInstanceRequest
            {
                DBInstanceIdentifier = dbInstanceId,
                AllocatedStorage = req.AllocatedStorageGb,
                DBInstanceClass = req.InstanceClass,
                Engine = req.Engine,
                MasterUsername = req.MasterUsername,
                MasterUserPassword = req.MasterUserPassword,
                PubliclyAccessible = req.PubliclyAccessible,
                MultiAZ = req.MultiAz,
                StorageType = req.StorageType,
                Port = req.Port,
                BackupRetentionPeriod = req.BackupRetentionDays,
                AutoMinorVersionUpgrade = req.AutoMinorVersionUpgrade,
                Tags =
                [
                    new() { Key = "Project", Value = EnsureUtils.canonicalPrefix },
                    new() { Key = "Name", Value = dbInstanceId }
                ]
            };

            // optional subnet group
            if (!string.IsNullOrWhiteSpace(req.DBSubnetGroupName)) createReq.DBSubnetGroupName = req.DBSubnetGroupName;
            if (!string.IsNullOrWhiteSpace(req.OptionGroupName)) createReq.OptionGroupName = req.OptionGroupName;
            if (!string.IsNullOrWhiteSpace(req.ParameterGroupName)) createReq.DBParameterGroupName = req.ParameterGroupName;
            if (!string.IsNullOrWhiteSpace(req.EngineVersion)) createReq.EngineVersion = req.EngineVersion;

            try
            {
                var resp = await _rds.CreateDBInstanceAsync(createReq, ct).ConfigureAwait(false);
            }
            catch (DBInstanceAlreadyExistsException)
            {
                // raced - treat as existing
                var racedInst = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
                var racedResult = new EnsureRdsResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    DBInstanceIdentifier = dbInstanceId,
                    Endpoint = racedInst?.Endpoint?.Address,
                    Message = "DB instance created concurrently by another actor",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = racedResult; }
                Console.WriteLine($"[EnsureRDS] Race: treat as existing {dbInstanceId}");
                return racedResult;
            }

            // Wait until available (simple polling)
            var available = await WaitForInstanceAvailableAsync(dbInstanceId, req.AvailabilityTimeoutSeconds, ct).ConfigureAwait(false);
            if (!available)
            {
                var failed = new EnsureRdsResult { Created = false, AlreadyExisted = false, DBInstanceIdentifier = dbInstanceId, Endpoint = null, Message = "Timed out waiting for DB instance to become available", LoggedRecords = Array.Empty<ResourceRecord>() };
                lock (_stateLock) { _lastResult = failed; }
                Console.WriteLine($"[EnsureRDS] Timed out waiting for {dbInstanceId}");
                return failed;
            }

            var inst = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
            var arn = inst?.DBInstanceArn ?? dbInstanceId;

            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, dbInstanceId, arn ?? dbInstanceId, _region);
            try
            {
                await _logger.appendAsync(rec).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureRDS] Warning: failed to append log for {dbInstanceId}: {ex.Message}");
            }

            var success = new EnsureRdsResult
            {
                Created = true,
                AlreadyExisted = false,
                DBInstanceIdentifier = dbInstanceId,
                Endpoint = inst?.Endpoint?.Address,
                Message = "DB instance created",
                LoggedRecords = new[] { rec }
            };
            lock (_stateLock) { _lastResult = success; }
            Console.WriteLine($"[EnsureRDS] Created DB instance: {dbInstanceId}");
            return success;
        }

        // EnsureExistsAsync - if idOrName provided filters by Name/Id; else returns all RDS records
        public async Task<EnsureRdsExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var entries = new List<EnsureRdsExistsEntry>();
            foreach (var rec in records)
            {
                ct.ThrowIfCancellationRequested();
                var exists = false;
                DBInstance? inst = null;
                try
                {
                    inst = await DescribeDbInstanceAsync(rec.Name, ct).ConfigureAwait(false);
                    exists = inst != null;
                }
                catch { exists = false; }

                entries.Add(new EnsureRdsExistsEntry { Record = rec, ExistsInCloud = exists, Endpoint = inst?.Endpoint?.Address });
            }

            return new EnsureRdsExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count(e => e.ExistsInCloud), Missing = entries.Count(e => !e.ExistsInCloud) };
        }

        // EnsureDestroyAsync - attempts to delete the DB instance and remove only its own log line(s)
        public async Task<EnsureRdsDestroyResult> EnsureDestroyAsync(string idOrName, bool skipFinalSnapshot = true, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) throw new ArgumentNullException(nameof(idOrName));
            ct.ThrowIfCancellationRequested();

            var all = _logger.readAll();
            var matches = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)
                                      && (string.Equals(r.Name, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, idOrName, StringComparison.OrdinalIgnoreCase)))
                             .ToList();

            if (matches.Count == 0)
            {
                // try to detect existence directly
                var inst = await DescribeDbInstanceAsync(idOrName, ct).ConfigureAwait(false);
                if (inst == null)
                {
                    return new EnsureRdsDestroyResult { Destroyed = false, NotFound = true, DBInstanceIdentifier = idOrName, RemovedRecords = Array.Empty<ResourceRecord>(), Message = "No log entry and instance not found" };
                }

                matches.Add(new ResourceRecord { EnsureIdentifier = EnsureIdentifier, ResourceType = ResourceTypeName, Name = inst.DBInstanceIdentifier, Id = inst.DBInstanceArn ?? inst.DBInstanceIdentifier, Region = _region, CreatedAt = DateTime.UtcNow });
            }

            var removed = new List<ResourceRecord>();
            var anyDeleted = false;
            foreach (var rec in matches)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var inst = await DescribeDbInstanceAsync(rec.Name, ct).ConfigureAwait(false);
                    if (inst == null)
                    {
                        TryRemoveLogRecord(rec);
                        continue;
                    }

                    var delReq = new DeleteDBInstanceRequest
                    {
                        DBInstanceIdentifier = rec.Name,
                        SkipFinalSnapshot = skipFinalSnapshot,
                    };

                    await _rds.DeleteDBInstanceAsync(delReq, ct).ConfigureAwait(false);

                    // wait for deletion (simple polling)
                    await WaitForInstanceDeletedAsync(rec.Name, 300, ct).ConfigureAwait(false);

                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    anyDeleted = true;
                    Console.WriteLine($"[EnsureRDS] Deleted DB instance: {rec.Name}");
                }
                catch (DBInstanceNotFoundException)
                {
                    TryRemoveLogRecord(rec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureRDS] Error deleting {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureRdsDestroyResult { Destroyed = anyDeleted, NotFound = removed.Count == 0, DBInstanceIdentifier = idOrName, RemovedRecords = removed, Message = anyDeleted ? "Deleted and removed log entries" : "No instance deleted" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("EnsureRDS Snapshot:");
                if (_lastRequest != null)
                {
                    sb.AppendLine($" BaseName={_lastRequest.BaseName}");
                    sb.AppendLine($" Engine={_lastRequest.Engine} InstanceClass={_lastRequest.InstanceClass}");
                    sb.AppendLine($" AllocatedStorageGb={_lastRequest.AllocatedStorageGb}");
                }
                else sb.AppendLine(" Request=null");

                if (_lastResult != null)
                {
                    sb.AppendLine($" Created={_lastResult.Created}");
                    sb.AppendLine($" AlreadyExisted={_lastResult.AlreadyExisted}");
                    sb.AppendLine($" DBInstanceIdentifier={_lastResult.DBInstanceIdentifier}");
                    sb.AppendLine($" Endpoint={_lastResult.Endpoint}");
                }
                else sb.AppendLine(" Result=null");

                return sb.ToString();
            }
        }

        #region helpers

        private async Task<DBInstance?> DescribeDbInstanceAsync(string dbInstanceIdentifier, CancellationToken ct)
        {
            try
            {
                var resp = await _rds.DescribeDBInstancesAsync(new DescribeDBInstancesRequest { DBInstanceIdentifier = dbInstanceIdentifier }, ct).ConfigureAwait(false);
                return resp.DBInstances?.FirstOrDefault();
            }
            catch (DBInstanceNotFoundException)
            {
                return null;
            }
            catch (AmazonRDSException ex)
            {
                // if it's not a not-found error, log and return null conservatively
                if (string.Equals(ex.ErrorCode, "DBInstanceNotFound", StringComparison.OrdinalIgnoreCase)) return null;
                Console.WriteLine($"[EnsureRDS] DescribeDBInstances error for {dbInstanceIdentifier}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureRDS] Unexpected error while describing {dbInstanceIdentifier}: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> WaitForInstanceAvailableAsync(string id, int timeoutSeconds, CancellationToken ct)
        {
            var max = Math.Max(1, timeoutSeconds);
            var delayMs = 5000;
            var attempts = (max * 1000) / delayMs;
            for (int i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                var inst = await DescribeDbInstanceAsync(id, ct).ConfigureAwait(false);
                if (inst != null && string.Equals(inst.DBInstanceStatus, "available", StringComparison.OrdinalIgnoreCase)) return true;
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
            return false;
        }

        private async Task WaitForInstanceDeletedAsync(string id, int timeoutSeconds, CancellationToken ct)
        {
            var max = Math.Max(1, timeoutSeconds);
            var delayMs = 5000;
            var attempts = (max * 1000) / delayMs;
            for (int i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();
                var inst = await DescribeDbInstanceAsync(id, ct).ConfigureAwait(false);
                if (inst == null) return;
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
            }
        }

        // Remove only this ensure's log lines
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
                Console.WriteLine($"[EnsureRDS] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public sealed class EnsureRdsRequest
        {
            // logical base name; used for DB instance identifier (will be canonicalized)
            public string BaseName { get; init; } = "rds";
            public string Engine { get; init; } = "sqlserver-ex";
            public string? EngineVersion { get; init; }
            public string InstanceClass { get; init; } = "db.t3.micro";
            public int AllocatedStorageGb { get; init; } = 20;
            public string MasterUsername { get; init; } = "admin";
            public string MasterUserPassword { get; init; } = "ChangeMe123!";
            public bool PubliclyAccessible { get; init; } = true;
            public bool MultiAz { get; init; } = false;
            public string StorageType { get; init; } = "gp2";
            public int Port { get; init; } = 1433;
            public int BackupRetentionDays { get; init; } = 0;
            public bool AutoMinorVersionUpgrade { get; init; } = true;
            public string? DBSubnetGroupName { get; init; }
            public string? ParameterGroupName { get; init; }
            public string? OptionGroupName { get; init; }
            public int AvailabilityTimeoutSeconds { get; init; } = 900; // wait up to 15 minutes by default
        }

        public sealed class EnsureRdsResult
        {
            public bool Created { get; init; }
            public bool AlreadyExisted { get; init; }
            public string? DBInstanceIdentifier { get; init; }
            public string? Endpoint { get; init; }
            public string? Message { get; init; }
            public IReadOnlyList<ResourceRecord> LoggedRecords { get; init; } = Array.Empty<ResourceRecord>();
        }

        public sealed class EnsureRdsExistsEntry
        {
            public ResourceRecord? Record { get; init; }
            public bool ExistsInCloud { get; init; }
            public string? Endpoint { get; init; }
        }

        public sealed class EnsureRdsExistsSummary
        {
            public IReadOnlyList<EnsureRdsExistsEntry> Entries { get; init; } = Array.Empty<EnsureRdsExistsEntry>();
            public int Total { get; init; }
            public int Found { get; init; }
            public int Missing { get; init; }
        }

        public sealed class EnsureRdsDestroyResult
        {
            public bool Destroyed { get; init; }
            public bool NotFound { get; init; }
            public string? DBInstanceIdentifier { get; init; }
            public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
            public string? Message { get; init; }
        }

        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureRDS.cs
//
// EnsureRDS
// - Idempotent EnsureCreateAsync, read-only EnsureExistsAsync, best-effort EnsureDestroyAsync for RDS instances
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureRDS"
// - Tags created resources with canonical project tag (EnsureUtils.canonicalPrefix)
// - Emits Console logs and writes single-line ResourceRecord entries via Infralogger
// - Designed to be testable and conservative: no destructive action without explicit destroy call
//
// Minimal IAM permissions (examples):
// - rds:DescribeDBInstances, rds:CreateDBInstance, rds:DeleteDBInstance, rds:AddTagsToResource, rds:ModifyDBInstance, rds:DescribeDBSubnetGroups
//
// Note: This file intentionally keeps operations simple and best-effort. Adjust timeouts, retry policies,
// parameter validation, multi-AZ decisions and snapshot handling as needed for production.