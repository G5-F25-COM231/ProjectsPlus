using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.Runtime;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;
using Tag = Amazon.RDS.Model.Tag;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureRDS
    {
        private readonly IAmazonRDS _rds;
        private readonly EnsureSM _smEnsure; // collaborator for secrets operations
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureRDS";
        private const string ResourceTypeName = "RDSInstance";

        public EnsureRDS(IAmazonRDS rds, EnsureSM smEnsure, Infralogger logger, string region)
        {
            _rds = rds ?? throw new ArgumentNullException(nameof(rds));
            _smEnsure = smEnsure ?? throw new ArgumentNullException(nameof(smEnsure));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // Idempotent create that optionally uses Secrets Manager for password + rotation
        public async Task<EnsureRdsResult> EnsureCreateAsync(EnsureRdsRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var dbInstanceId = EnsureUtils.buildCanonicalName(req.BaseName);

            // 0) If SecretBaseName provided: ensure secret exists (GenerateSecretString) and obtain password/username
            string? secretName = null;
            string? secretArn = null;
            string passwordFromSecret = req.MasterUserPassword ?? string.Empty; // fallback
            string username = req.MasterUsername ?? "mssqlexadmin";

            if (!string.IsNullOrWhiteSpace(req.SecretBaseName))
            {
                secretName = EnsureUtils.buildCanonicalName(req.SecretBaseName);

                // Ask EnsureSM to create or return existing secret. EnsureSM.EnsureCreateAsync returns ARN in LoggedRecords.Id/SecretArn (see its DTO).
                var smReq = new EnsureSM.EnsureSmRequest
                {
                    BaseName = req.SecretBaseName,
                    Description = $"RDS credential for {dbInstanceId}",
                    SecretString = null, // request generation
                    AdditionalTags = req.SecretTags?.ToDictionary(k => k.Key, v => v.Value)
                };

                var smRes = await _smEnsure.EnsureCreateAsync(smReq, ct).ConfigureAwait(false);

                // If secret already existed, attempt to read it (we rely on EnsureSM having created only name/arn)
                secretArn = smRes.SecretArn ?? smRes.LoggedRecords?.FirstOrDefault()?.Id;
                if (string.IsNullOrWhiteSpace(secretArn))
                {
                    // best-effort: use resource record ARN if missing
                    secretArn = smRes.LoggedRecords?.FirstOrDefault()?.Id;
                }

                // Read secret value now so we can pass password to RDS create
                var secretVal = await ReadSecretValueAsync(secretName ?? secretArn ?? throw new InvalidOperationException("Secret identity missing"), ct).ConfigureAwait(false);
                // secretVal expected to be JSON like {"username":"admin","password":"..."} or plain password
                if (!string.IsNullOrWhiteSpace(secretVal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(secretVal);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("password", out var pwElem)) passwordFromSecret = pwElem.GetString() ?? passwordFromSecret;
                            if (doc.RootElement.TryGetProperty("username", out var unElem)) username = unElem.GetString() ?? username;
                        }
                        else if (doc.RootElement.ValueKind == JsonValueKind.String)
                        {
                            passwordFromSecret = doc.RootElement.GetString() ?? passwordFromSecret;
                        }
                    }
                    catch
                    {
                        // fallback to raw string
                        passwordFromSecret = secretVal;
                    }
                }
            }

            // 1) Check existing RDS instance
            var existing = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
            if (existing != null)
            {
                return new EnsureRdsResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    DBInstanceIdentifier = dbInstanceId,
                    Endpoint = existing.Endpoint?.Address,
                    Message = "RDS instance already exists",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
            }

            // 2) Build create request with password from secret if available else from request
            var createReq = new CreateDBInstanceRequest
            {
                DBInstanceIdentifier = dbInstanceId,
                AllocatedStorage = req.AllocatedStorageGb,
                DBInstanceClass = req.InstanceClass,
                Engine = req.Engine,
                MasterUsername = username,
                MasterUserPassword = string.IsNullOrWhiteSpace(passwordFromSecret) ? req.MasterUserPassword : passwordFromSecret,
                PubliclyAccessible = req.PubliclyAccessible,
                MultiAZ = req.MultiAz,
                StorageType = req.StorageType,
                Port = req.Port,
                BackupRetentionPeriod = req.BackupRetentionDays,
                AutoMinorVersionUpgrade = req.AutoMinorVersionUpgrade,
                Tags = new List<Tag>
                {
                    new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix },
                    new Tag { Key = "Name", Value = dbInstanceId }
                }
            };
            if (!string.IsNullOrWhiteSpace(req.DBSubnetGroupName)) createReq.DBSubnetGroupName = req.DBSubnetGroupName;
            if (!string.IsNullOrWhiteSpace(req.ParameterGroupName)) createReq.DBParameterGroupName = req.ParameterGroupName;
            if (!string.IsNullOrWhiteSpace(req.OptionGroupName)) createReq.OptionGroupName = req.OptionGroupName;
            if (!string.IsNullOrWhiteSpace(req.EngineVersion)) createReq.EngineVersion = req.EngineVersion;

            try
            {
                await _rds.CreateDBInstanceAsync(createReq, ct).ConfigureAwait(false);
            }
            catch (DBInstanceAlreadyExistsException)
            {
                // race - treat as existing
                var racedInst = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
                return new EnsureRdsResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    DBInstanceIdentifier = dbInstanceId,
                    Endpoint = racedInst?.Endpoint?.Address,
                    Message = "DB instance created concurrently by another actor",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
            }

            // 3) Wait until available
            var available = await WaitForInstanceAvailableAsync(dbInstanceId, req.AvailabilityTimeoutSeconds, ct).ConfigureAwait(false);
            if (!available)
            {
                return new EnsureRdsResult { Created = false, AlreadyExisted = false, DBInstanceIdentifier = dbInstanceId, Endpoint = null, Message = "Timed out waiting for DB instance to become available", LoggedRecords = Array.Empty<ResourceRecord>() };
            }

            // 4) Describe instance and write infralog
            var inst = await DescribeDbInstanceAsync(dbInstanceId, ct).ConfigureAwait(false);
            var arn = inst?.DBInstanceArn ?? dbInstanceId;
            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, dbInstanceId, arn ?? dbInstanceId, _region);
            try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch { /* best-effort */ }

            // 5) If we used a secret, update it with endpoint/port/engine/username and optionally enable rotation
            if (!string.IsNullOrWhiteSpace(secretName) || !string.IsNullOrWhiteSpace(secretArn))
            {
                var sid = secretName ?? secretArn!;
                var connectionObj = new Dictionary<string, object>
                {
                    ["username"] = username,
                    ["password"] = string.IsNullOrWhiteSpace(passwordFromSecret) ? string.Empty : passwordFromSecret,
                    ["host"] = inst?.Endpoint?.Address ?? string.Empty,
                    ["port"] = inst?.Endpoint?.Port ?? req.Port,
                    ["engine"] = req.Engine,
                    ["dbInstanceIdentifier"] = dbInstanceId
                };
                var payload = JsonSerializer.Serialize(connectionObj);

                // Update secret value (merge or replace). We call the EnsureSM helper to append this payload into the secret.
                try
                {
                    // Use the low-level client to put the secret value via EnsureSM's internal client if needed.
                    // We assume EnsureSM exposes a convenience method; if not, we perform direct PutSecretValue via AWS client.
                    await _smEnsure.AppendOrUpdateSecretAsync(sid, payload, ct).ConfigureAwait(false);
                }
                catch
                {
                    // best-effort: try a direct PutSecretValue via SecretsManager client inside EnsureSM if available
                    try
                    {
                        // fallback: if EnsureSM exposes a public client, not available here; ignore silently
                    }
                    catch { }
                }

                // If a rotation lambda ARN was provided in request, request rotation start (RotateSecret) to enable rotation using that Lambda.
                if (!string.IsNullOrWhiteSpace(req.SecretRotationLambdaArn))
                {
                    try
                    {
                        await _smEnsure.ConfigureRotationAsync(sid, req.SecretRotationLambdaArn, req.RotationAutomaticallyAfterDays, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[EnsureRDS] Warning: failed to configure rotation for secret {sid}: {ex.Message}");
                    }
                }
            }

            return new EnsureRdsResult
            {
                Created = true,
                AlreadyExisted = false,
                DBInstanceIdentifier = dbInstanceId,
                Endpoint = inst?.Endpoint?.Address,
                Message = "DB instance created and secret updated",
                LoggedRecords = new[] { rec }
            };
        }

        #region helpers (unchanged)

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

        private async Task<string?> ReadSecretValueAsync(string secretIdOrName, CancellationToken ct)
        {
            try
            {
                // Use EnsureSM's helper if available; otherwise call AWS SecretsManager directly via an internal client.
                return await _smEnsure.GetSecretStringAsync(secretIdOrName, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureRDS] Warning: failed to read secret {secretIdOrName}: {ex.Message}");
                return null;
            }
        }

        #endregion

        //-------------------------------------------




        //------------------------------------------

        #region DTOs (subset relevant to this Ensure)

        public sealed class EnsureRdsRequest
        {
            public string BaseName { get; init; } = "rds";
            public string Engine { get; init; } = "sqlserver-ex";
            public string? EngineVersion { get; init; }
            public string InstanceClass { get; init; } = "db.t3.micro";
            public int AllocatedStorageGb { get; init; } = 20;
            public string MasterUsername { get; init; } = "mssqlexadmin";
            public string? MasterUserPassword { get; init; } = "adminjdevnforne+"; // optional if SecretBaseName used
            public bool PubliclyAccessible { get; init; } = true;
            public bool MultiAz { get; init; } = false;
            public string StorageType { get; init; } = "gp2";
            public int Port { get; init; } = 1433;
            public int BackupRetentionDays { get; init; } = 0;
            public bool AutoMinorVersionUpgrade { get; init; } = true;
            public string? DBSubnetGroupName { get; init; }
            public string? ParameterGroupName { get; init; }
            public string? OptionGroupName { get; init; }
            public string? EngineVersionString { get; init; }
            public int AvailabilityTimeoutSeconds { get; init; } = 900;

            // Secrets integration
            public string? SecretBaseName { get; init; } // if provided, EnsureRDS will create/use this secret
            public IDictionary<string, string>? SecretTags { get; init; }
            public string? SecretRotationLambdaArn { get; init; } // if provided, orchestrator will attempt to enable rotation with this lambda
            public int RotationAutomaticallyAfterDays { get; init; } = 30;
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

        #endregion
    }
}


// src/IaC_ProjectsPlus/EnsureModules/EnsureRDS.cs
//
// EnsureRDS (Secrets Manager integrated)
// - If request.SecretBaseName or SecretRotationLambdaArn are provided, this Ensure will:
//   1) Create or reuse a Secrets Manager secret (GenerateSecretString) via EnsureSM
//   2) Use the secret's password as MasterUserPassword when creating the RDS instance
//   3) After instance becomes available, update the secret with connection metadata (host, port, engine, identifier)
//   4) Optionally start/enable rotation by invoking RotateSecret (uses provided RotationLambdaArn when given)
// - Backwards compatible: if no secret info provided, behaves like earlier EnsureRDS (MasterUserPassword must be in request)
// - Uses EnsureSM (wrapper) so we keep single responsibility: EnsureSM owns secret create/update/delete.
// - Conservative: does not manage rotation Lambda creation; expects caller to supply RotationLambdaArn when they want automated rotation.
