using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.IdentityManagement;
using Amazon.IdentityManagement.Model;
using t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules;

namespace t5f25sdprojectone_projectsplus.IaC_ProjectsPlus.EnsureModules
{
    public sealed class EnsureIAM
    {
        private readonly IAmazonIdentityManagementService _iam;
        private readonly Infralogger _logger;
        private readonly string _region;

        private const string EnsureIdentifier = "EnsureIAM";
        private const string ResourceTypeName = "IamProfile";

        private EnsureIamRequest? _lastRequest;
        private EnsureIamResult? _lastResult;
        private readonly object _stateLock = new();

        public EnsureIAM(IAmazonIdentityManagementService iam, Infralogger logger, string region)
        {
            _iam = iam ?? throw new ArgumentNullException(nameof(iam));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-2" : region;
        }

        // Create or return existing role (assumes trust policy provided)
        public async Task<EnsureRoleResult> EnsureRoleAsync(EnsureRoleRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var roleName = EnsureUtils.buildCanonicalName(req.RoleName);
            lock (_stateLock) { _lastRequest = new EnsureIamRequest { RoleName = req.RoleName }; _lastResult = null; }

            // Try get role
            try
            {
                var get = await _iam.GetRoleAsync(new GetRoleRequest { RoleName = roleName }, ct).ConfigureAwait(false);
                var existing = get.Role;
                var result = new EnsureRoleResult { Created = false, AlreadyExisted = true, RoleArn = existing.Arn, RoleName = existing.RoleName, Message = "Role exists" };
                lock (_stateLock) { _lastResult = new EnsureIamResult { Message = result.Message }; }
                Console.WriteLine($"[EnsureIAM] Role already exists: {roleName}");
                return result;
            }
            catch (NoSuchEntityException) { /* create */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureIAM] Error querying role {roleName}: {ex.Message}");
            }

            // Create role
            try
            {
                var createResp = await _iam.CreateRoleAsync(new CreateRoleRequest
                {
                    RoleName = roleName,
                    AssumeRolePolicyDocument = req.AssumeRolePolicyDocument,
                    Description = req.Description,
                    Tags = [new Tag { Key = "Project", Value = EnsureUtils.canonicalPrefix }, new Tag { Key = "Name", Value = roleName }]
                }, ct).ConfigureAwait(false);

                var role = createResp.Role;
                var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, ResourceTypeName, roleName, role.Arn ?? role.RoleId ?? roleName, _region);
                try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch { }

                var result = new EnsureRoleResult { Created = true, AlreadyExisted = false, RoleArn = role.Arn, RoleName = role.RoleName, Message = "Role created", LogRecord = rec };
                lock (_stateLock) { _lastResult = new EnsureIamResult { Message = result.Message }; }
                Console.WriteLine($"[EnsureIAM] Created role {roleName} arn={role.Arn}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureIAM] Error creating role {roleName}: {ex.Message}");
                return new EnsureRoleResult { Created = false, AlreadyExisted = false, Message = ex.Message };
            }
        }

        // Ensure a managed policy exists (returns ARN)
        public async Task<EnsurePolicyResult> EnsurePolicyAsync(EnsurePolicyRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            var policyName = EnsureUtils.buildCanonicalName(req.PolicyName);
            var policyDoc = string.IsNullOrWhiteSpace(req.PolicyDocument) ? "{}" : req.PolicyDocument;
            lock (_stateLock) { _lastRequest = new EnsureIamRequest { PolicyName = req.PolicyName }; _lastResult = null; }

            // Try find by name (ListPolicies filter)
            try
            {
                var list = await _iam.ListPoliciesAsync(new ListPoliciesRequest { Scope = PolicyScopeType.Local, OnlyAttached = false }, ct).ConfigureAwait(false);
                var found = list.Policies?.FirstOrDefault(p => string.Equals(p.PolicyName, policyName, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    return new EnsurePolicyResult { Created = false, AlreadyExisted = true, PolicyArn = found.Arn, PolicyName = found.PolicyName };
                }
            }
            catch { }

            // Create policy
            try
            {
                var create = await _iam.CreatePolicyAsync(new CreatePolicyRequest
                {
                    PolicyName = policyName,
                    PolicyDocument = policyDoc,
                    Description = req.Description
                }, ct).ConfigureAwait(false);

                var pol = create.Policy;
                var rec = EnsureUtils.makeResourceRecord(EnsureIdentifier, "IamPolicy", policyName, pol.Arn ?? policyName, _region);
                try { await _logger.appendAsync(rec).ConfigureAwait(false); } catch { }

                return new EnsurePolicyResult { Created = true, AlreadyExisted = false, PolicyArn = pol.Arn, PolicyName = pol.PolicyName, LogRecord = rec };
            }
            catch (EntityAlreadyExistsException)
            {
                // race
                var list2 = await _iam.ListPoliciesAsync(new ListPoliciesRequest { Scope = PolicyScopeType.Local }, ct).ConfigureAwait(false);
                var p2 = list2.Policies?.FirstOrDefault(p => p.PolicyName == policyName);
                return new EnsurePolicyResult { Created = false, AlreadyExisted = true, PolicyArn = p2?.Arn, PolicyName = p2?.PolicyName };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureIAM] Error creating policy {policyName}: {ex.Message}");
                return new EnsurePolicyResult { Created = false, AlreadyExisted = false, Message = ex.Message };
            }
        }

        // Attach a policy (by ARN) to a role
        public async Task<AttachPolicyResult> AttachPolicyAsync(AttachPolicyRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            ct.ThrowIfCancellationRequested();

            try
            {
                await _iam.AttachRolePolicyAsync(new AttachRolePolicyRequest { RoleName = EnsureUtils.buildCanonicalName(req.RoleName), PolicyArn = req.PolicyArn }, ct).ConfigureAwait(false);
                Console.WriteLine($"[EnsureIAM] Attached policy {req.PolicyArn} to role {req.RoleName}");
                return new AttachPolicyResult { Attached = true, Message = "Attached" };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnsureIAM] Error attaching policy {req.PolicyArn} to role {req.RoleName}: {ex.Message}");
                return new AttachPolicyResult { Attached = false, Message = ex.Message };
            }
        }

        // EnsureExistsAsync: return IAM-related records from infralog (roles/policies)
        public Task<EnsureExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var entries = records.Select(r => new EnsureExistsEntry { Name = r.Name, Id = r.Id, ResourceType = r.ResourceType, LoggedAt = r.CreatedAt }).ToList();
            var summary = new EnsureExistsSummary { Entries = entries, Total = entries.Count, Found = entries.Count, Missing = 0 };
            return Task.FromResult(summary);
        }

        // EnsureDestroyAsync: best-effort detach & delete roles/policies created by this ensure
        public async Task<EnsureDestroyResult> EnsureDestroyAsync(string? idOrName = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            var all = _logger.readAll();
            var records = all.Where(r => string.Equals(r.EnsureIdentifier, EnsureIdentifier, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                var key = idOrName.Trim();
                records = records.Where(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Id, key, StringComparison.OrdinalIgnoreCase));
            }

            var removed = new List<ResourceRecord>();
            foreach (var rec in records.OrderByDescending(r => r.CreatedAt))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (rec.ResourceType == ResourceTypeName)
                    {
                        var info = JsonSerializer.Deserialize<IamProfile>(rec.Id);
                        if (info != null && !string.IsNullOrWhiteSpace(info.RoleName))
                        {
                            // detach inline managed policies then delete
                            try
                            {
                                var attached = await _iam.ListAttachedRolePoliciesAsync(new ListAttachedRolePoliciesRequest { RoleName = info.RoleName }, ct).ConfigureAwait(false);
                                foreach (var ap in attached.AttachedPolicies ?? Enumerable.Empty<AttachedPolicyType>())
                                {
                                    try { await _iam.DetachRolePolicyAsync(new DetachRolePolicyRequest { RoleName = info.RoleName, PolicyArn = ap.PolicyArn }, ct).ConfigureAwait(false); } catch { }
                                }
                            }
                            catch { }

                            try { await _iam.DeleteRoleAsync(new DeleteRoleRequest { RoleName = info.RoleName }, ct).ConfigureAwait(false); } catch { }
                        }
                    }
                    else if (rec.ResourceType == "IamPolicy")
                    {
                        // delete policy (must detach first)
                        try
                        {
                            var arn = rec.Id;
                            var versions = await _iam.ListPolicyVersionsAsync(new ListPolicyVersionsRequest { PolicyArn = arn }, ct).ConfigureAwait(false);
                            foreach (var v in versions.Versions.Where(v => (bool)!v.IsDefaultVersion))
                            {
                                try { await _iam.DeletePolicyVersionAsync(new DeletePolicyVersionRequest { PolicyArn = arn, VersionId = v.VersionId }, ct).ConfigureAwait(false); } catch { }
                            }
                            try { await _iam.DeletePolicyAsync(new DeletePolicyRequest { PolicyArn = arn }, ct).ConfigureAwait(false); } catch { }
                        }
                        catch { }
                    }

                    TryRemoveLogRecord(rec);
                    removed.Add(rec);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[EnsureIAM] Error destroying record {rec.Name}: {ex.Message}");
                }
            }

            return new EnsureDestroyResult { Removed = removed, RemovedCount = removed.Count, Message = removed.Count > 0 ? "Destroy attempts complete" : "No records removed" };
        }

        public override string ToString()
        {
            lock (_stateLock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("EnsureIAM Snapshot:");
                if (_lastRequest != null) sb.AppendLine($" RoleName={_lastRequest.RoleName} PolicyName={_lastRequest.PolicyName}");
                else sb.AppendLine(" Request=null");
                if (_lastResult != null) sb.AppendLine($" Message={_lastResult.Message}");
                else sb.AppendLine(" Result=null");
                return sb.ToString();
            }
        }

        #region helpers

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
                Console.WriteLine($"[EnsureIAM] Warning: failed to remove log record for {rec.Name}: {ex.Message}");
            }
        }

        #endregion

        #region DTOs / VOs

        public sealed class EnsureRoleRequest { public string RoleName { get; init; } = "ecs-exec-role"; public string AssumeRolePolicyDocument { get; init; } = "{}"; public string? Description { get; init; } }
        public sealed class EnsureRoleResult { public bool Created { get; init; } public bool AlreadyExisted { get; init; } public string? RoleArn { get; init; } public string? RoleName { get; init; } public string? Message { get; init; } public ResourceRecord? LogRecord { get; init; } }

        public sealed class EnsurePolicyRequest { public string PolicyName { get; init; } = "ecs-task-policy"; public string? PolicyDocument { get; init; } public string? Description { get; init; } }
        public sealed class EnsurePolicyResult { public bool Created { get; init; } public bool AlreadyExisted { get; init; } public string? PolicyArn { get; init; } public string? PolicyName { get; init; } public string? Message { get; init; } public ResourceRecord? LogRecord { get; init; } }

        public sealed class AttachPolicyRequest { public string RoleName { get; init; } = "ecs-exec-role"; public string PolicyArn { get; init; } = string.Empty; }
        public sealed class AttachPolicyResult { public bool Attached { get; init; } public string? Message { get; init; } }

        public sealed class IamProfile { public string RoleName { get; init; } = string.Empty; public string? RoleArn { get; init; } }
        public sealed class EnsureIamRequest { public string? RoleName { get; init; } public string? PolicyName { get; init; } }
        public sealed class EnsureIamResult { public string? Message { get; init; } }

        public sealed class EnsureExistsEntry { public string Name { get; init; } = string.Empty; public string Id { get; init; } = string.Empty; public string ResourceType { get; init; } = string.Empty; public DateTime LoggedAt { get; init; } }
        public sealed class EnsureExistsSummary { public IReadOnlyList<EnsureExistsEntry> Entries { get; init; } = Array.Empty<EnsureExistsEntry>(); public int Total { get; init; } public int Found { get; init; } public int Missing { get; init; } }
        public sealed class EnsureDestroyResult { public IReadOnlyList<ResourceRecord> Removed { get; init; } = Array.Empty<ResourceRecord>(); public int RemovedCount { get; init; } public string? Message { get; init; } }

        #endregion
    }
}

// src/IaC_ProjectsPlus/EnsureModules/EnsureIAM.cs
//
// EnsureIAM
// - Ensures IAM roles and policies required by ECS tasks (execution role, task role) and RDS access are present.
// - Uses EnsureUtils.buildCanonicalName and EnsureUtils.makeResourceRecord to persist ResourceRecord entries with EnsureIdentifier = "EnsureIAM".
// - Adds canonical project tag where possible (note: IAM tags require specific permissions).
// - Exposes methods to create roles, inline policies or managed policies, attach policies, and to query existence.
// - Idempotent ensures and best-effort destroy (detach & delete).
//
// Public API (high level)
// - Task<EnsureRoleResult> EnsureRoleAsync(EnsureRoleRequest req, CancellationToken ct = default)
// - Task<EnsurePolicyResult> EnsurePolicyAsync(EnsurePolicyRequest req, CancellationToken ct = default)
// - Task<AttachPolicyResult> AttachPolicyAsync(AttachPolicyRequest req, CancellationToken ct = default)
// - Task<EnsureExistsSummary> EnsureExistsAsync(string? idOrName = null, CancellationToken ct = default)
// - Task<EnsureDestroyResult> EnsureDestroyAsync(string? idOrName = null, CancellationToken ct = default)
//
// Notes
// - Uses IAmazonIdentityManagementService (IAM) from AWS SDK.
// - For production, craft least-privilege policies. This module provides scaffolding and sample task/execution policies.
// - EnsureIAM does not attempt to create instance profiles (not needed for Fargate). It produces role ARNs you can pass to EnsureECS TaskDefinition requests.
//
