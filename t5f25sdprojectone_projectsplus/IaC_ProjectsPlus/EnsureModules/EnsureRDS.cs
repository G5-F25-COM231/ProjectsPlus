using System.Text.Json;
using Amazon;
using Amazon.EC2;
using Amazon.RDS;
using Amazon.RDS.Model;
using Amazon.SecretsManager;
using Newtonsoft.Json;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureASM;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureDDB;
using static t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules.EnsureVPC;
using Tag = Amazon.RDS.Model.Tag;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureRDS
    {
        private readonly IAmazonRDS _rds;
        private readonly IAmazonEC2 _ec2;
        private readonly IAmazonSecretsManager _asm;
        private readonly EnsureASM _smEnsure; // collaborator for secrets operations
        private readonly EnsureVPC _vpcEnsure;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureRDS";
        private const string ResourceTypeName = "RDSInstance";

        public EnsureRDS(IAmazonRDS rds, IAmazonEC2 ec2, IAmazonSecretsManager asm, Infralogger logger, string region)
        {
            _rds = rds ?? throw new ArgumentNullException(nameof(rds));
            _ec2 = ec2 ?? throw new ArgumentNullException(nameof(ec2));
            _asm = asm ?? throw new ArgumentNullException(nameof(asm));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;

            var reg = RegionEndpoint.USEast2.SystemName;
            _smEnsure = new EnsureASM(_asm, logger, reg);
            _vpcEnsure = new EnsureVPC(_ec2, logger, reg);
        }

        // Idempotent create that optionally uses Secrets Manager for password + rotation
        public async Task<EnsureRdsResult> EnsureCreateAsync(EnsureRdsRequest req, CancellationToken ct = default)
        {
            var vpcInfra = await _vpcEnsure.EnsureVpcAsync(new EnsureVpcRequest() { CallerCidr = GetMyPublicIpAsync().GetAwaiter().GetResult() }, ct);
            var asmInfra = await _smEnsure.EnsureCreateAsync(new EnsureSmRequest() { Description = "dbcreds" }, ct);



            async Task<string> GetDbSubnet()
            { //*******************************************

                var dbSubnetGroup = EnsureUtils.buildCanonicalName(req.BaseName) + "_dbsbntgrp";
                bool exists = false;
                try
                {
                    var desc = await _rds.DescribeDBSubnetGroupsAsync(new DescribeDBSubnetGroupsRequest { DBSubnetGroupName = dbSubnetGroup }, ct);
                    exists = desc.DBSubnetGroups?.Count > 0;
                }
                catch (DBSubnetGroupNotFoundException) { exists = false; }

                if (!exists)
                {
                    Console.WriteLine($"Creating DB subnet group '{dbSubnetGroup}'...");
                    await _rds.CreateDBSubnetGroupAsync(new CreateDBSubnetGroupRequest
                    {
                        DBSubnetGroupName = dbSubnetGroup,
                        DBSubnetGroupDescription = "Public subnet group for RDS instance",
                        SubnetIds = [.. vpcInfra.Profile.PublicSubnetIds] //vpcInfo.SubnetIds //------change to private subnets in production
                    }, ct);
                    return dbSubnetGroup;
                }
                else
                {
                    Console.WriteLine($"DB subnet group '{dbSubnetGroup}' already exists.");
                    return dbSubnetGroup;
                }

            }

            req.DBSubnetGroupName = await GetDbSubnet();
            req.VpcSecurityGroupIds = [vpcInfra.Profile.RdsSecurityGroupId];

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
                var smReq = new EnsureASM.EnsureSmRequest
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
            //var createReq = new CreateDBInstanceRequest
            //{
            //    DBInstanceIdentifier = dbInstanceId,
            //    AllocatedStorage = req.AllocatedStorageGb,
            //    DBInstanceClass = req.InstanceClass,
            //    Engine = req.Engine,
            //    MasterUsername = username,
            //    MasterUserPassword = string.IsNullOrWhiteSpace(passwordFromSecret) ? req.MasterUserPassword : passwordFromSecret,
            //    PubliclyAccessible = req.PubliclyAccessible,
            //    MultiAZ = req.MultiAz,
            //    StorageType = req.StorageType,
            //    Port = req.Port,
            //    BackupRetentionPeriod = req.BackupRetentionDays,
            //    AutoMinorVersionUpgrade = req.AutoMinorVersionUpgrade,
            //    Tags = new List<Tag>
            //    {
            //        new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix },
            //        new Tag { Key = "Name", Value = dbInstanceId }
            //    }
            //};          

            // assume dbInstanceId, username and passwordFromSecret are available in scope as in your snippet
            var createReq = new CreateDBInstanceRequest
            {
                // identifiers
                DBInstanceIdentifier = dbInstanceId,
                DBName = string.IsNullOrWhiteSpace(req.DBName) ? null : req.DBName,

                // core settings
                AllocatedStorage = req.AllocatedStorageGb,
                DBInstanceClass = req.InstanceClass,
                Engine = req.Engine,
                EngineVersion = string.IsNullOrWhiteSpace(req.EngineVersionString) ? req.EngineVersion : req.EngineVersionString,
                MasterUsername = username,
                MasterUserPassword = string.IsNullOrWhiteSpace(passwordFromSecret) ? req.MasterUserPassword : passwordFromSecret,
                PubliclyAccessible = req.PubliclyAccessible,
                MultiAZ = req.MultiAz,
                StorageType = req.StorageType,
                Port = req.Port,
                BackupRetentionPeriod = req.BackupRetentionDays,
                AutoMinorVersionUpgrade = req.AutoMinorVersionUpgrade,

                // placement / windows
                AvailabilityZone = string.IsNullOrWhiteSpace(req.AvailabilityZone) ? null : req.AvailabilityZone,
                PreferredMaintenanceWindow = string.IsNullOrWhiteSpace(req.PreferredMaintenanceWindow) ? null : req.PreferredMaintenanceWindow,
                PreferredBackupWindow = string.IsNullOrWhiteSpace(req.PreferredBackupWindow) ? null : req.PreferredBackupWindow,

                // storage / performance
                Iops = req.Iops,
                MaxAllocatedStorage = req.MaxAllocatedStorage,
                StorageThroughput = req.StorageThroughput,
                StorageEncrypted = req.StorageEncrypted,
                KmsKeyId = string.IsNullOrWhiteSpace(req.KmsKeyId) ? null : req.KmsKeyId,

                // snapshot / deletion
                CopyTagsToSnapshot = req.CopyTagsToSnapshot,
                DeletionProtection = req.DeletionProtection,

                // monitoring / IAM
                MonitoringInterval = req.MonitoringInterval,
                MonitoringRoleArn = string.IsNullOrWhiteSpace(req.MonitoringRoleArn) ? null : req.MonitoringRoleArn,
                EnableIAMDatabaseAuthentication = req.EnableIAMDatabaseAuthentication,

                // performance insights
                EnablePerformanceInsights = req.EnablePerformanceInsights,
                PerformanceInsightsKMSKeyId = string.IsNullOrWhiteSpace(req.PerformanceInsightsKMSKeyId) ? null : req.PerformanceInsightsKMSKeyId,
                PerformanceInsightsRetentionPeriod = req.PerformanceInsightsRetentionPeriod,

                // engine / license / options
                LicenseModel = string.IsNullOrWhiteSpace(req.LicenseModel) ? null : req.LicenseModel,
                CharacterSetName = string.IsNullOrWhiteSpace(req.CharacterSetName) ? null : req.CharacterSetName,
                CACertificateIdentifier = string.IsNullOrWhiteSpace(req.CACertificateIdentifier) ? null : req.CACertificateIdentifier,
                DBClusterIdentifier = string.IsNullOrWhiteSpace(req.DBClusterIdentifier) ? null : req.DBClusterIdentifier,
                Domain = string.IsNullOrWhiteSpace(req.Domain) ? null : req.Domain,
                DomainIAMRoleName = string.IsNullOrWhiteSpace(req.DomainIAMRoleName) ? null : req.DomainIAMRoleName,
                PromotionTier = req.PromotionTier,

                // logs / processor features
                EnableCloudwatchLogsExports = req.EnableCloudwatchLogsExports?.ToList(),
                ProcessorFeatures = req.ProcessorFeatures?.ToList(),
                //UseDefaultProcessorFeatures = req.UseDefaultProcessorFeatures,

                // TDE
                TdeCredentialArn = string.IsNullOrWhiteSpace(req.TdeCredentialArn) ? null : req.TdeCredentialArn,
                TdeCredentialPassword = string.IsNullOrWhiteSpace(req.TdeCredentialPassword) ? null : req.TdeCredentialPassword,

                // replica / source
                //SourceDBInstanceIdentifier = string.IsNullOrWhiteSpace(req.SourceDBInstanceIdentifier) ? null : req.SourceDBInstanceIdentifier,

                // networking / subnet / security groups                
                DBSubnetGroupName = string.IsNullOrWhiteSpace(req.DBSubnetGroupName) ? null : req.DBSubnetGroupName,
                VpcSecurityGroupIds = req.VpcSecurityGroupIds?.ToList(),
                EnableCustomerOwnedIp = req.EnableCustomerOwnedIp,

                // tags: prefer explicit Tags from req, otherwise use your default tags
                Tags = (req.Tags != null && req.Tags.Count > 0)
                    ? req.Tags.ToList()
                    : new List<Tag> {
                        new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix },
                        new Tag { Key = "Name", Value = dbInstanceId },
                        new Tag { Key = "Environment", Value = "dev" }
                    }
            };

            var replicaReq = new CreateDBInstanceReadReplicaRequest // for if a replica is needed.
            {
                DBInstanceIdentifier = createReq.DBInstanceIdentifier + "_" + "replica_a",
                SourceDBInstanceIdentifier = createReq.DBInstanceIdentifier,
                DBInstanceClass = req.InstanceClass,
                // ...other fields as needed...
            };


            // Note: CreateDBInstanceRequest properties that are left null will be omitted by the SDK.
            // Validate that any fields that must be present for your engine (e.g., Iops for io1) are set in req before calling CreateDBInstanceAsync.

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
                    LoggedRecords = [new ResourceRecord { EnsureIdentifier = System.Text.Json.JsonSerializer.Serialize(racedInst) }]
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
                var payload = System.Text.Json.JsonSerializer.Serialize(connectionObj);

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


        public async Task<EnsureExistsSummary> EnsureExistsAsync(string? dbInstanceNameOrArn = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var all = _logger.readAll() ?? Array.Empty<ResourceRecord>();
            var rdsRecords = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(dbInstanceNameOrArn))
            {
                var key = dbInstanceNameOrArn.Trim();
                rdsRecords = rdsRecords.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var entries = new List<EnsureExistsEntry>();
            foreach (var rec in rdsRecords)
            {
                ct.ThrowIfCancellationRequested();
                var exists = await DescribeDbInstanceAsync(rec.Name, ct).ConfigureAwait(false) != null;
                entries.Add(new EnsureExistsEntry { TableName = rec.Name, TableArn = rec.Id, LoggedAt = rec.CreatedAt, ExistsInCloud = exists });
            }

            var summary = new EnsureExistsSummary
            {
                Entries = entries,
                Total = entries.Count,
                Found = entries.Count(e => e.ExistsInCloud),
                Missing = entries.Count(e => !e.ExistsInCloud)
            };

            return summary;
        }

        public async Task<EnsureDestroyResult> EnsureDestroyAsync(string dbInstanceNameOrArn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dbInstanceNameOrArn)) throw new ArgumentNullException(nameof(dbInstanceNameOrArn));
            ct.ThrowIfCancellationRequested();

            var all = _logger.readAll() ?? Array.Empty<ResourceRecord>();
            var matches = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(r.ResourceType, ResourceTypeName, StringComparison.OrdinalIgnoreCase)
                                      && (string.Equals(r.Name, dbInstanceNameOrArn, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, dbInstanceNameOrArn, StringComparison.OrdinalIgnoreCase)))
                             .ToList();

            if (matches.Count == 0)
            {
                // If no log entry but instance exists in cloud, synthesize a record so we can attempt deletion
                var existsInCloud = await DescribeDbInstanceAsync(dbInstanceNameOrArn, ct).ConfigureAwait(false) != null;
                if (!existsInCloud)
                {
                    return new EnsureDestroyResult
                    {
                        Destroyed = false,
                        NotFound = true,
                        DBName = dbInstanceNameOrArn,
                        Message = "No log entry and instance not found",
                        RemovedRecords = Array.Empty<ResourceRecord>()
                    };
                }

                matches.Add(new ResourceRecord { EnsureIdentifier = EnsureIdentifier, ResourceType = ResourceTypeName, Name = dbInstanceNameOrArn, Id = dbInstanceNameOrArn, Region = _region, CreatedAt = DateTime.UtcNow });
            }

            var removed = new List<ResourceRecord>();
            var anyDeleted = false;

            foreach (var rec in matches)
            {
                ct.ThrowIfCancellationRequested();
                var exists = await DescribeDbInstanceAsync(rec.Name, ct).ConfigureAwait(false) != null;
                if (!exists)
                {
                    // remove only log lines that match this EnsureIdentifier and the exact Name/Id
                    TryRemoveLogRecord(rec);
                    continue;
                }

                try
                {
                    // DeleteDBInstance: choose to skip final snapshot for best-effort destroy; callers can change behavior if needed.
                    var delReq = new DeleteDBInstanceRequest
                    {
                        DBInstanceIdentifier = rec.Name,
                        SkipFinalSnapshot = true,
                        DeleteAutomatedBackups = true
                    };

                    await _rds.DeleteDBInstanceAsync(delReq, ct).ConfigureAwait(false);

                    // Wait for deletion to complete
                    await WaitForInstanceDeletedAsync(rec.Name, ct).ConfigureAwait(false);

                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    anyDeleted = true;
                    Console.WriteLine($"[EnsureRDS] Deleted instance: {rec.Name}");
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

            return new EnsureDestroyResult
            {
                Destroyed = anyDeleted,
                NotFound = removed.Count == 0,
                DBName = dbInstanceNameOrArn,
                Message = anyDeleted ? "Deleted and removed log entries" : "No instance deleted",
                RemovedRecords = removed
            };

            // local helpers used by this method
            async Task WaitForInstanceDeletedAsync(string id, CancellationToken token)
            {
                const int maxAttempts = 20;
                const int delayMs = 1000;
                for (int i = 0; i < maxAttempts; i++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var inst = await DescribeDbInstanceAsync(id, token).ConfigureAwait(false);
                        if (inst == null) return;
                    }
                    catch (DBInstanceNotFoundException) { return; }
                    catch { /* ignore transient */ }

                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                }
            }

            void TryRemoveLogRecord(ResourceRecord rec)
            {
                try
                {
                    var lines = _logger.readAll()?.ToList() ?? new List<ResourceRecord>();
                    var matchesLocal = lines.Where(r =>
                        string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.ResourceType, rec.ResourceType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Name, rec.Name, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Id, rec.Id, StringComparison.OrdinalIgnoreCase)).ToList();

                    if (matchesLocal.Count == 0) return;

                    var kept = lines.Except(matchesLocal).ToList();
                    _logger.clear();
                    foreach (var k in kept) _logger.appendAsync(k).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureRDS] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
                }
            }
        }


        //------------------------------------------

        #region DTOs (subset relevant to this Ensure)

        public sealed class EnsureRdsRequest
        {
            // original fields
            public string BaseName { get; init; } = "rds";
            public string Engine { get; init; } = "sqlserver-ex";
            public string? EngineVersion { get; init; }
            public string InstanceClass { get; init; } = "db.t3.micro";
            public int AllocatedStorageGb { get; init; } = 20;
            public string MasterUsername { get; init; } = "mssqlexadmin";
            public string? MasterUserPassword { get; init; } = "adminjdevnforne+";
            public bool PubliclyAccessible { get; init; } = true;
            public bool MultiAz { get; init; } = false;
            public string StorageType { get; init; } = "gp2";
            public int Port { get; init; } = 1433;
            public int BackupRetentionDays { get; init; } = 0;
            public bool AutoMinorVersionUpgrade { get; init; } = true;
            public string? DBSubnetGroupName { get; set; }
            public string? ParameterGroupName { get; init; }
            public string? OptionGroupName { get; init; }
            public string? EngineVersionString { get; init; }
            public int AvailabilityTimeoutSeconds { get; init; } = 900;

            // Secrets integration
            public string? SecretBaseName { get; init; }
            public IDictionary<string, string>? SecretTags { get; init; }
            public string? SecretRotationLambdaArn { get; init; }
            public int RotationAutomaticallyAfterDays { get; init; } = 30;

            // VPC / SG / subnet options
            public string? VpcId { get; init; }
            public IList<string>? VpcSecurityGroupIds { get; set; }
            public bool CreateSecurityGroup { get; init; } = false;
            public string? SecurityGroupName { get; init; }
            public string? SecurityGroupDescription { get; init; }
            public IDictionary<string, string>? SecurityGroupTags { get; init; }
            public IList<string>? SubnetIds { get; init; }

            // --- Added CreateDBInstanceRequest-like members (not previously present) ---

            // identifiers / naming
            public string? DBInstanceIdentifier { get; init; }            // optional explicit identifier
            public string? DBName { get; init; }                         // initial DB name

            // availability / placement
            public string? AvailabilityZone { get; init; }
            public string? PreferredMaintenanceWindow { get; init; }
            public string? PreferredBackupWindow { get; init; }

            // storage / IOPS / autoscaling
            public int? Iops { get; init; }
            public int? MaxAllocatedStorage { get; init; }               // for autoscaling
            public int? StorageThroughput { get; init; }                 // gp3 throughput
            public bool? StorageEncrypted { get; init; }
            public string? KmsKeyId { get; init; }

            // snapshot / deletion
            public bool? CopyTagsToSnapshot { get; init; }
            public bool? DeletionProtection { get; init; }

            // monitoring / IAM
            public int? MonitoringInterval { get; init; }
            public string? MonitoringRoleArn { get; init; }
            public bool? EnableIAMDatabaseAuthentication { get; init; }

            // performance insights
            public bool? EnablePerformanceInsights { get; init; }
            public string? PerformanceInsightsKMSKeyId { get; init; }
            public int? PerformanceInsightsRetentionPeriod { get; init; }

            // licensing / engine options
            public string? LicenseModel { get; init; }
            public string? CharacterSetName { get; init; }
            public string? CACertificateIdentifier { get; init; }

            // cluster / domain / promotion
            public string? DBClusterIdentifier { get; init; }
            public string? Domain { get; init; }
            public string? DomainIAMRoleName { get; init; }
            public int? PromotionTier { get; init; }

            // logs / processor features / advanced
            public IList<string>? EnableCloudwatchLogsExports { get; init; }
            public IList<ProcessorFeature>? ProcessorFeatures { get; init; }
            public bool? UseDefaultProcessorFeatures { get; init; }

            // TDE
            public string? TdeCredentialArn { get; init; }
            public string? TdeCredentialPassword { get; init; }

            // replica / source
            public string? SourceDBInstanceIdentifier { get; init; }     // for read-replicas or restore-from

            // networking / IP
            public bool? EnableCustomerOwnedIp { get; init; }

            // tags (AWS Tag objects)
            public IList<Tag>? Tags { get; init; }

            // any other CreateDBInstanceRequest fields you want to add later can be appended here
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

    public class EnsureDestroyResult
    {
        public bool Destroyed { get; set; }
        public bool NotFound { get; set; }
        public string DBName { get; set; }
        public string Message { get; set; }
        public IList<ResourceRecord> RemovedRecords { get; set; }
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
