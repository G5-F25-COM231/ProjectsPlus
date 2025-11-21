// src/Auth/InMemory/InMemoryAuthorizationService.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Services.Interfaces;
using IAuthorizationService = t5f25sdprojectone_projectsplus.Services.Interfaces.IAuthorizationService;

namespace t5f25sdprojectone_projectsplus.Services.Authorization
{
    /// <summary>
    /// Deterministic, in-memory AuthorizationService intended for dev and tests.
    /// - Evaluates a PermissionPolicy (in declared rule order).
    /// - Enforces Deny-wins semantics: any matching Deny yields overall Deny.
    /// - Returns AuthorizationResult with ExplainText and Sources (rule ids) for auditability and deterministic tests.
    /// - Role membership is supplied via a simple per-user role map to keep tests deterministic.
    /// - No external dependencies (DB/cache); clients may combine this with AuthorizationCache in integration tests.
    /// </summary>
    public class InMemoryAuthorizationService : IAuthorizationService
    {
        private readonly PermissionPolicy _policy;
        private readonly ConcurrentDictionary<long, HashSet<string>> _userRoles;

        /// <summary>
        /// Create a deterministic in-memory service.
        /// - policy: PermissionPolicy instance (rules evaluated in order).
        /// - roleMembership: optional map of userId -> roles for principal resolution.
        /// </summary>
        public InMemoryAuthorizationService(PermissionPolicy policy, IDictionary<long, IEnumerable<string>>? roleMembership = null)
        {
            _policy = policy ?? new PermissionPolicy();
            _userRoles = new ConcurrentDictionary<long, HashSet<string>>();

            if (roleMembership != null)
            {
                foreach (var kv in roleMembership)
                {
                    var set = new HashSet<string>(kv.Value ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                    _userRoles[kv.Key] = set;
                }
            }

        }

        public Task InvalidateUserCacheAsync(long userId)
        {
            // Stateless role map; allow tests to remove roles by calling this + manipulating roleMembership externally.
            _userRoles.TryRemove(userId, out _);
            return Task.CompletedTask;
        }

        public Task<AuthorizationResult> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default)
        {
            var matching = EvaluateMatchingRules(userId, action, resourceType, resourceId).ToList();

            // Deny-wins: if any Deny match exists, return Deny with combined explain + sources.
            var denyRules = matching.Where(r => string.Equals(r.Effect, "Deny", StringComparison.OrdinalIgnoreCase)).ToList();
            if (denyRules.Any())
            {
                var explain = "Deny-wins: " + string.Join(" | ", denyRules.Select(r => r.Explain ?? r.Id));
                var sources = denyRules.Select(r => r.Id).ToList();
                return Task.FromResult(AuthorizationResult.Deny(explain, sources.ToArray()));
            }

            // If any Audit rules matched but no Allow, return Audit (non-allowing) including sources.
            var auditRules = matching.Where(r => string.Equals(r.Effect, "Audit", StringComparison.OrdinalIgnoreCase)).ToList();
            var allowRules = matching.Where(r => string.Equals(r.Effect, "Allow", StringComparison.OrdinalIgnoreCase)).ToList();

            if (allowRules.Any())
            {
                // Choose first Allow (deterministic)
                var rule = allowRules.First();
                var explain = rule.Explain ?? $"Allowed by rule {rule.Id}";
                var sources = allowRules.Select(r => r.Id).ToList();
                return Task.FromResult(AuthorizationResult.Allow(explain, sources.ToArray()));
            }

            if (auditRules.Any())
            {
                var explain = "Audit-only matches: " + string.Join(" | ", auditRules.Select(r => r.Explain ?? r.Id));
                var sources = auditRules.Select(r => r.Id).ToList();
                return Task.FromResult(AuthorizationResult.Audit(explain, sources.ToArray()));
            }

            // Default deny when no rules matched
            return Task.FromResult(AuthorizationResult.Deny("No matching allow rule", Array.Empty<string>()));
        }

        public Task<IReadOnlyDictionary<long, AuthorizationResult>> IsAuthorizedBatchAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default)
        {
            var dict = new Dictionary<long, AuthorizationResult>(resourceIds.Count);
            foreach (var id in resourceIds)
            {
                // Synchronous call to keep determinism and avoid interleaving in tests
                dict[id] = IsAuthorizedAsync(userId, action, resourceType, id, ct).GetAwaiter().GetResult();
            }
            return Task.FromResult((IReadOnlyDictionary<long, AuthorizationResult>)dict);
        }

        // Helper: deterministic evaluation of policy rules in declared order
        private IEnumerable<PolicyRule> EvaluateMatchingRules(long userId, string action, string? resourceType, long? resourceId)
        {
            foreach (var rule in _policy.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Action)) continue;
                if (!string.Equals(rule.Action, action, StringComparison.OrdinalIgnoreCase)) continue;

                // Principal matching
                var principals = rule.Principals;
                var principalMatched = false;

                if (principals?.UserIds != null && principals.UserIds.Contains(userId)) principalMatched = true;

                if (!principalMatched && principals?.Roles != null && principals.Roles.Count > 0)
                {
                    if (_userRoles.TryGetValue(userId, out var roles))
                    {
                        if (principals.Roles.Any(r => roles.Contains(r))) principalMatched = true;
                    }
                }

                // If rule has no principals specified, treat as global (matches all principals)
                if (!principalMatched && (principals == null || (principals.UserIds == null || principals.UserIds.Count == 0) && (principals.Roles == null || principals.Roles.Count == 0) && (principals.Groups == null || principals.Groups.Count == 0)))
                {
                    principalMatched = true;
                }

                if (!principalMatched) continue;

                // Resource matching
                if (rule.Resource != null)
                {
                    if (!string.Equals(rule.Resource.Type, resourceType, StringComparison.OrdinalIgnoreCase)) continue;
                    if (rule.Resource.Ids != null && resourceId.HasValue && !rule.Resource.Ids.Contains(resourceId.Value)) continue;
                }

                yield return rule;
            }
        }
    }
}
