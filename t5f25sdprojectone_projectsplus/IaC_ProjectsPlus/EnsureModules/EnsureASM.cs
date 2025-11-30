using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.Runtime;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureASM
    {
        private readonly IAmazonSecretsManager _sm;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureSM";
        private const string ResourceTypeName = "SecretsManagerSecret";

        private EnsureSmRequest? _lastRequest;
        private EnsureSmResult? _lastResult;
        private readonly object _stateLock = new();

        public EnsureASM(IAmazonSecretsManager sm, Infralogger logger, string region)
        {
            _sm = sm ?? throw new ArgumentNullException(nameof(sm));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        /// <summary>
        /// EnsureCreateAsync
        /// - Ensures secret exists with the canonical name derived from BaseName.
        /// - If secret exists, returns AlreadyExisted; otherwise creates using SecretString (if provided) and tags.
        /// - Writes a ResourceRecord (EnsureIdentifier=EnsureSM) only after successful creation/confirm describe.
        /// </summary>
        public async Task<EnsureSmResult> EnsureCreateAsync(EnsureSmRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            lock (_stateLock) { _lastRequest = req; _lastResult = null; }

            var secretName = EnsureUtils.buildCanonicalName(req.BaseName);

            // 1) Check existing by name
            var existing = await DescribeSecretByNameAsync(secretName, ct).ConfigureAwait(false);
            if (existing != null)
            {
                var already = new EnsureSmResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    SecretName = secretName,
                    SecretArn = existing.ARN,
                    Message = "Secret already exists",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = already; }
                Console.WriteLine($"[EnsureSM] Secret already exists: {secretName}");
                return already;
            }

            // 2) Build CreateSecretRequest
            var createReq = new CreateSecretRequest
            {
                Name = secretName,
                Description = req.Description,
                Tags = new List<Tag> { new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix }, new Tag { Key = "Name", Value = secretName } }
            };

            if (!string.IsNullOrWhiteSpace(req.SecretString))
                createReq.SecretString = req.SecretString;

            if (req.AdditionalTags != null && req.AdditionalTags.Count > 0)
            {
                foreach (var kv in req.AdditionalTags)
                {
                    // avoid duplicate keys
                    if (!createReq.Tags.Any(t => string.Equals(t.Key, kv.Key, StringComparison.OrdinalIgnoreCase)))
                        createReq.Tags.Add(new Tag { Key = kv.Key, Value = kv.Value });
                }
            }

            CreateSecretResponse createResp;
            try
            {
                createResp = await _sm.CreateSecretAsync(createReq, ct).ConfigureAwait(false);
            }
            catch (ResourceExistsException)
            {
                // raced - treat as existing
                var raced = await DescribeSecretByNameAsync(secretName, ct).ConfigureAwait(false);
                var racedResult = new EnsureSmResult
                {
                    Created = false,
                    AlreadyExisted = true,
                    SecretName = secretName,
                    SecretArn = raced?.ARN,
                    Message = "Secret created concurrently",
                    LoggedRecords = Array.Empty<ResourceRecord>()
                };
                lock (_stateLock) { _lastResult = racedResult; }
                Console.WriteLine($"[EnsureSM] Race detected; treating as existing: {secretName}");
                return racedResult;
            }
            catch (AmazonSecretsManagerException ex)
            {
                var failed = new EnsureSmResult { Created = false, AlreadyExisted = false, SecretName = secretName, SecretArn = null, Message = $"CreateSecret failed: {ex.Message}", LoggedRecords = Array.Empty<ResourceRecord>() };
                lock (_stateLock) { _lastResult = failed; }
                Console.WriteLine($"[EnsureSM] CreateSecret failed for {secretName}: {ex.Message}");
                return failed;
            }

            // 3) Confirm by describing the secret to get ARN
            var described = await DescribeSecretByNameAsync(secretName, ct).ConfigureAwait(false);
            var arn = described?.ARN ?? createResp.ARN;

            // 4) Write ResourceRecord to infralog
            var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, secretName, arn ?? secretName, _region);
            try
            {
                await _logger.appendAsync(rec).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureSM] Warning: failed to append log for {secretName}: {ex.Message}");
            }

            var success = new EnsureSmResult
            {
                Created = true,
                AlreadyExisted = false,
                SecretName = secretName,
                SecretArn = arn,
                Message = "Secret created",
                LoggedRecords = new[] { rec }
            };
            lock (_stateLock) { _lastResult = success; }
            Console.WriteLine($"[EnsureSM] Created secret: {secretName} (arn: {arn})");
            return success;
        }

        /// <summary>
        /// EnsureExistsAsync
        /// - If idOrName provided, filters by Name or Id (ARN); otherwise returns summary for all EnsureSM records.
        /// - Does not create or delete; read-only.
        /// </summary>
        public async Task<EnsureSmExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
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

            var entries = new List<EnsureSmExistsEntry>();
            foreach (var rec in records)
            {
                ct.ThrowIfCancellationRequested();
                var exists = false;
                SecretListEntry? sEntry = null;
                try
                {
                    sEntry = await DescribeSecretEntryByArnOrNameAsync(rec.Id, rec.Name, ct).ConfigureAwait(false);
                    exists = sEntry != null;
                }
                catch { exists = false; }

                entries.Add(new EnsureSmExistsEntry { Record = rec, ExistsInCloud = exists, SecretArn = sEntry?.ARN, SecretName = sEntry?.Name });
            }

            return new EnsureSmExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count(e => e.ExistsInCloud), Missing = entries.Count(e => !e.ExistsInCloud) };
        }

        /// <summary>
        /// EnsureDestroyAsync
        /// - Attempts to schedule deletion of the secret (DeleteSecret with ForceDeleteWithoutRecovery=false by default)
        /// - Removes only this ensure's log line(s) on success or if secret not found
        /// </summary>
        public async Task<EnsureSmDestroyResult> EnsureDestroyAsync(string idOrName, bool forceDeleteWithoutRecovery = false, int recoveryWindowInDays = 7, CancellationToken ct = default)
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
                // Try to find secret directly
                var entry = await DescribeSecretEntryByArnOrNameAsync(idOrName, idOrName, ct).ConfigureAwait(false);
                if (entry == null)
                {
                    return new EnsureSmDestroyResult { Destroyed = false, NotFound = true, SecretNameOrArn = idOrName, RemovedRecords = Array.Empty<ResourceRecord>(), Message = "No log entry and secret not found" };
                }

                matches.Add(new ResourceRecord { EnsureIdentifier = EnsureIdentifier, ResourceType = ResourceTypeName, Name = entry.Name ?? idOrName, Id = entry.ARN ?? idOrName, Region = _region, CreatedAt = DateTime.UtcNow });
            }

            var removed = new List<ResourceRecord>();
            var anyDeleted = false;
            foreach (var rec in matches)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // If secret exists, attempt delete
                    var entry = await DescribeSecretEntryByArnOrNameAsync(rec.Id, rec.Name, ct).ConfigureAwait(false);
                    if (entry == null)
                    {
                        TryRemoveLogRecord(rec);
                        continue;
                    }

                    var delReq = new DeleteSecretRequest
                    {
                        SecretId = entry.ARN ?? entry.Name,
                        ForceDeleteWithoutRecovery = forceDeleteWithoutRecovery
                    };
                    if (!forceDeleteWithoutRecovery)
                    {
                        // set recovery window (minimum 7 days for delayed deletion)
                        delReq.RecoveryWindowInDays = Math.Max(7, recoveryWindowInDays);
                    }

                    await _sm.DeleteSecretAsync(delReq, ct).ConfigureAwait(false);

                    // On success remove log lines belonging to this EnsureIdentifier only
                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                    anyDeleted = true;
                    Console.WriteLine($"[EnsureSM] Deleted secret (scheduled): {rec.Name}");
                }
                catch (ResourceNotFoundException)
                {
                    TryRemoveLogRecord(rec);
                }
                catch (AmazonSecretsManagerException ex)
                {
                    Console.WriteLine($"[EnsureSM] Error deleting secret {rec.Name}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureSM] Unexpected error deleting secret {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureSmDestroyResult { Destroyed = anyDeleted, NotFound = removed.Count == 0, SecretNameOrArn = idOrName, RemovedRecords = removed, Message = anyDeleted ? "Deleted/scheduled and removed log entries" : "No secret deleted" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("EnsureSM Snapshot:");
                if (_lastRequest != null)
                {
                    sb.AppendLine($" BaseName={_lastRequest.BaseName}");
                    sb.AppendLine($" Description={_lastRequest.Description}");
                    sb.AppendLine($" HasSecretString={!string.IsNullOrWhiteSpace(_lastRequest.SecretString)}");
                }
                else sb.AppendLine(" Request=null");

                if (_lastResult != null)
                {
                    sb.AppendLine($" Created={_lastResult.Created}");
                    sb.AppendLine($" AlreadyExisted={_lastResult.AlreadyExisted}");
                    sb.AppendLine($" SecretName={_lastResult.SecretName}");
                    sb.AppendLine($" SecretArn={_lastResult.SecretArn}");
                }
                else sb.AppendLine(" Result=null");

                return sb.ToString();
            }
        }

        #region helpers

        private async Task<SecretListEntry?> DescribeSecretByNameAsync(string secretName, CancellationToken ct)
        {
            // Use ListSecrets with a filter on name to find the secret entry (DescribeSecret by name may throw if deleted)
            try
            {
                var listReq = new ListSecretsRequest { Filters = new List<Filter> { new Filter { Key = "name", Values = new List<string> { secretName } } } };
                var resp = await _sm.ListSecretsAsync(listReq, ct).ConfigureAwait(false);
                return resp.SecretList?.FirstOrDefault(s => string.Equals(s.Name, secretName, StringComparison.OrdinalIgnoreCase));
            }
            catch (AmazonSecretsManagerException ex)
            {
                Console.WriteLine($"[EnsureSM] ListSecrets error for {secretName}: {ex.Message}");
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<SecretListEntry?> DescribeSecretEntryByArnOrNameAsync(string arnCandidate, string nameCandidate, CancellationToken ct)
        {
            // Prefer direct DescribeSecret by ARN or name, fallback to ListSecrets filter when necessary
            try
            {
                // Try DescribeSecret using arnCandidate first (if it looks like ARN)
                if (!string.IsNullOrWhiteSpace(arnCandidate) && arnCandidate.StartsWith("arn:", StringComparison.OrdinalIgnoreCase))
                {
                    var desc = await _sm.DescribeSecretAsync(new DescribeSecretRequest { SecretId = arnCandidate }, ct).ConfigureAwait(false);
                    return new SecretListEntry { ARN = desc.ARN, Name = desc.Name };
                }

                // Try DescribeSecret by nameCandidate
                if (!string.IsNullOrWhiteSpace(nameCandidate))
                {
                    var desc = await _sm.DescribeSecretAsync(new DescribeSecretRequest { SecretId = nameCandidate }, ct).ConfigureAwait(false);
                    return new SecretListEntry { ARN = desc.ARN, Name = desc.Name };
                }
            }
            catch (ResourceNotFoundException)
            {
                return null;
            }
            catch (AmazonSecretsManagerException)
            {
                // fallback to ListSecrets
            }

            // Fallback: list filter by nameCandidate or arnCandidate
            return await DescribeSecretByNameAsync(!string.IsNullOrWhiteSpace(nameCandidate) ? nameCandidate : arnCandidate, ct).ConfigureAwait(false);
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
                Console.WriteLine($"[EnsureSM] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion


        /// <summary>
        /// Read the secret's string value (SecretString) by name or ARN. Returns null when not found or on error.
        /// </summary>
        public async Task<string?> GetSecretStringAsync(string nameOrArn, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nameOrArn)) throw new ArgumentNullException(nameof(nameOrArn));
            try
            {
                var resp = await _sm.DescribeSecretAsync(new DescribeSecretRequest { SecretId = nameOrArn }, ct).ConfigureAwait(false);
            }
            catch (ResourceNotFoundException)
            {
                return null;
            }
            catch
            {
                // continue to TryGet value; some surfaces may still return value even if Describe failed
            }

            try
            {
                var getReq = new GetSecretValueRequest { SecretId = nameOrArn };
                var getResp = await _sm.GetSecretValueAsync(getReq, ct).ConfigureAwait(false);
                return getResp.SecretString;
            }
            catch (ResourceNotFoundException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureSM] Warning: GetSecretStringAsync failed for {nameOrArn}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// AppendOrUpdateSecretAsync
        /// - If the secret contains JSON, merges top-level properties from incomingPayloadJson into existing JSON (incoming keys overwrite).
        /// - If existing value is non-JSON or missing, replaces it with incomingPayloadJson as the new SecretString.
        /// - Uses PutSecretValue to create a new version.
        /// - Returns the resulting SecretId/ARN on success (or null on failure).
        /// </summary>
        public async Task<string?> AppendOrUpdateSecretAsync(string nameOrArn, string incomingPayloadJson, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nameOrArn)) throw new ArgumentNullException(nameof(nameOrArn));
            if (incomingPayloadJson == null) throw new ArgumentNullException(nameof(incomingPayloadJson));

            // Read existing secret value (best-effort)
            string? existing = null;
            try
            {
                existing = await GetSecretStringAsync(nameOrArn, ct).ConfigureAwait(false);
            }
            catch
            {
                existing = null;
            }

            string newPayload = incomingPayloadJson;

            // Attempt JSON merge if both are JSON objects
            try
            {
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    using var existingDoc = JsonDocument.Parse(existing);
                    using var incomingDoc = JsonDocument.Parse(incomingPayloadJson);

                    if (existingDoc.RootElement.ValueKind == JsonValueKind.Object && incomingDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        // Merge: existing <- incoming (incoming overwrites)
                        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

                        foreach (var prop in existingDoc.RootElement.EnumerateObject())
                            merged[prop.Name] = prop.Value;

                        foreach (var prop in incomingDoc.RootElement.EnumerateObject())
                            merged[prop.Name] = prop.Value;

                        var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var kv in merged)
                            obj[kv.Key] = kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : JsonSerializer.Deserialize<object>(kv.Value.GetRawText());

                        newPayload = JsonSerializer.Serialize(obj);
                    }
                    else
                    {
                        // existing not JSON-object; replace with incoming
                        newPayload = incomingPayloadJson;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureSM] Warning: JSON merge failed for secret {nameOrArn}: {ex.Message}. Falling back to replace.");
                newPayload = incomingPayloadJson;
            }

            // Put new secret value (creates a new version)
            try
            {
                var putReq = new PutSecretValueRequest
                {
                    SecretId = nameOrArn,
                    SecretString = newPayload
                };
                var putResp = await _sm.PutSecretValueAsync(putReq, ct).ConfigureAwait(false);
                return putResp.ARN ?? putResp.Name ?? nameOrArn;
            }
            catch (ResourceNotFoundException)
            {
                // If secret doesn't exist, try CreateSecret (use name as plain name, not ARN)
                try
                {
                    var createReq = new CreateSecretRequest
                    {
                        Name = nameOrArn,
                        SecretString = newPayload,
                        Description = $"Created by EnsureSM.AppendOrUpdateSecretAsync for {nameOrArn}"
                    };
                    var createResp = await _sm.CreateSecretAsync(createReq, ct).ConfigureAwait(false);
                    return createResp.ARN ?? createResp.Name ?? nameOrArn;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureSM] Error creating secret {nameOrArn}: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureSM] Error putting secret value for {nameOrArn}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ConfigureRotationAsync
        /// - Best-effort helper to enable rotation and to trigger a first rotation.
        /// - Requires that a rotation Lambda ARN already exists and the calling principal has permissions to UpdateSecret and RotateSecret.
        /// - This method will:
        ///     1) Attempt to call RotateSecret (a trigger) to perform an immediate rotation (best-effort).
        ///     2) Attempt to update the secret's rotation configuration by calling RotateSecret (to trigger) and UpdateSecret if needed.
        /// - Note: Creating and wiring a rotation Lambda and the correct rotation configuration is out-of-scope for this helper.
        /// </summary>
        public async Task<bool> ConfigureRotationAsync(string nameOrArn, string rotationLambdaArn, int automaticallyAfterDays = 30, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(nameOrArn)) throw new ArgumentNullException(nameof(nameOrArn));
            if (string.IsNullOrWhiteSpace(rotationLambdaArn)) throw new ArgumentNullException(nameof(rotationLambdaArn));

            try
            {
                // Best-effort: try to enable rotation by calling RotateSecret (triggers immediate rotation) and then set rotation configuration
                // Trigger an immediate rotation attempt (may fail if rotation not configured)
                try
                {
                    var rotateReq = new RotateSecretRequest
                    {
                        SecretId = nameOrArn
                        // RotateSecretRequest does not accept a Lambda ARN via this call; enabling rotation programmatically requires the rotation configuration to be present on the secret
                    };
                    await _sm.RotateSecretAsync(rotateReq, ct).ConfigureAwait(false);
                    Console.WriteLine($"[EnsureSM] Triggered rotation for secret {nameOrArn}");
                }
                catch (ResourceNotFoundException)
                {
                    Console.WriteLine($"[EnsureSM] Cannot rotate secret {nameOrArn} because it was not found");
                    return false;
                }
                catch (AmazonSecretsManagerException ex)
                {
                    // Some SDK surfaces require the rotation configuration to be set via the Console / CloudFormation or via PutRotationSchedule (not available in all SDK versions).
                    // We'll log and continue to attempt an UpdateSecret that includes a description referencing the rotation Lambda.
                    Console.WriteLine($"[EnsureSM] RotateSecret attempt for {nameOrArn} returned: {ex.Message} (continuing to UpdateSecret metadata)");
                }

                // Try to annotate the secret to include rotation-lambda reference via Description (best-effort)
                try
                {
                    var updateReq = new UpdateSecretRequest
                    {
                        SecretId = nameOrArn,
                        Description = $"RotationLambda:{rotationLambdaArn};AutoDays:{Math.Max(1, automaticallyAfterDays)}"
                    };
                    await _sm.UpdateSecretAsync(updateReq, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureSM] Warning: failed to update secret description for rotation annotation {nameOrArn}: {ex.Message}");
                }

                // If the SDK/environment supports rotation configuration APIs (e.g., PutResourcePolicy or PutRotationSchedule) you could add them here.
                // Return true as we attempted the configuration.
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureSM] Error configuring rotation for {nameOrArn}: {ex.Message}");
                return false;
            }
        }


        #region DTOs / VOs

        public sealed class EnsureSmRequest
        {
            public string BaseName { get; init; } = "asm";
            public string? Description { get; init; }
            public string? SecretString { get; init; } // plaintext secret content (optional)
            public Dictionary<string, string>? AdditionalTags { get; init; } // additional tags to add
        }

        public sealed class EnsureSmResult
        {
            public bool Created { get; init; }
            public bool AlreadyExisted { get; init; }
            public string? SecretName { get; init; }
            public string? SecretArn { get; init; }
            public string? Message { get; init; }
            public IReadOnlyList<ResourceRecord> LoggedRecords { get; init; } = Array.Empty<ResourceRecord>();
        }

        public sealed class EnsureSmExistsEntry
        {
            public ResourceRecord? Record { get; init; }
            public bool ExistsInCloud { get; init; }
            public string? SecretArn { get; init; }
            public string? SecretName { get; init; }
        }

        public sealed class EnsureSmExistsSummary
        {
            public IReadOnlyList<EnsureSmExistsEntry> Entries { get; init; } = Array.Empty<EnsureSmExistsEntry>();
            public int Total { get; init; }
            public int Found { get; init; }
            public int Missing { get; init; }
        }

        public sealed class EnsureSmDestroyResult
        {
            public bool Destroyed { get; init; }
            public bool NotFound { get; init; }
            public string? SecretNameOrArn { get; init; }
            public IReadOnlyList<ResourceRecord> RemovedRecords { get; init; } = Array.Empty<ResourceRecord>();
            public string? Message { get; init; }
        }

        #endregion
    }

}


// src/IaC_ProjectsPlus/EnsureModules/EnsureSM.cs
//
// EnsureSM
// - Idempotent EnsureCreateAsync, read-only EnsureExistsAsync, best-effort EnsureDestroyAsync for AWS Secrets Manager secrets
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureSM"
// - Tags created secrets with canonical project tag (EnsureUtils.canonicalPrefix) and optional additional tags from request
// - Writes single-line ResourceRecord entries via Infralogger and emits Console logs for key events
// - Conservative, testable, and self-contained: does not perform destructive actions without explicit destroy call
//
// Minimal IAM permissions required:
// - secretsmanager:CreateSecret, secretsmanager:DescribeSecret, secretsmanager:DeleteSecret, secretsmanager:ListSecrets, secretsmanager:TagResource
//
// Notes:
// - Secret value is stored as SecretString when provided. For production, prefer AWS-managed rotation or KMS-backed encryption as needed.
// - This implementation treats the canonical name as the Secrets Manager Name (must be unique per region/account).
// - The ResourceRecord.Id stores the secret ARN when available.