// src/Auth/Models/PermissionPolicy.cs
using System;
using System.Collections.Generic;

namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    /// <summary>
    /// JSON-backed permission policy model for AuthorizationService.
    /// - Rules are evaluated in declared order to ensure deterministic outcomes for tests and audits.
    /// - Each rule contains principal selectors (roles, users, groups), an action, an effect (Deny|Allow|Audit),
    ///   optional resource constraints, and an optional human-readable explanation.
    /// - Runtime evaluation enforces Deny-wins semantics: any matching Deny causes an overall Deny for that check.
    /// </summary>
    public class PermissionPolicy
    {
        /// <summary>
        /// Schema version. Increment when changing rule semantics.
        /// </summary>
        public string Version { get; set; } = "1";

        /// <summary>
        /// Deterministic ordered list of policy rules.
        /// </summary>
        public IList<PolicyRule> Rules { get; set; } = new List<PolicyRule>();
    }

    public class PolicyRule
    {
        /// <summary>
        /// Stable identifier for the rule; surfaced in ExplainText and Sources for auditability.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Effect values:
        /// - "Deny"  : explicit deny (highest priority)
        /// - "Allow" : explicit allow
        /// - "Audit" : no allow/deny decision but record for audit/telemetry
        /// </summary>
        public string Effect { get; set; } = "Deny";

        /// <summary>
        /// Action name this rule targets, e.g. "Project.View" or "Project.Edit".
        /// Matching is case-insensitive.
        /// </summary>
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Which principals this rule applies to. Any principal selector matching the request
        /// marks the rule as a candidate.
        /// </summary>
        public PrincipalSelector Principals { get; set; } = new PrincipalSelector();

        /// <summary>
        /// Optional resource constraints. If present, resource Type must match and resource id(s)
        /// must include the evaluated resource id (if provided).
        /// </summary>
        public ResourceSelector? Resource { get; set; }

        /// <summary>
        /// Optional free-form explanation surfaced in logs and tests.
        /// </summary>
        public string? Explain { get; set; }
    }

    public class PrincipalSelector
    {
        /// <summary>
        /// Role names (e.g., "Admin", "ProjectManager"). Matching is case-insensitive.
        /// </summary>
        public IList<string>? Roles { get; set; }

        /// <summary>
        /// Specific user ids this rule targets.
        /// </summary>
        public IList<long>? UserIds { get; set; }

        /// <summary>
        /// Groups or tenant identifiers; optional and may be used by callers who populate group context.
        /// </summary>
        public IList<string>? Groups { get; set; }
    }

    public class ResourceSelector
    {
        /// <summary>
        /// Resource type string, e.g., "Project".
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// If provided, matches only when the resource id is one of these values.
        /// Empty/NULL means any id of the given Type.
        /// </summary>
        public IList<long>? Ids { get; set; }
    }
}
