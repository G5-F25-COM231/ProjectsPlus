// src/Auth/AuthorizationService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Rich authorization surface consumed by controllers, workers and services.
    /// Implementations must return AuthorizationResult which includes decision, human-friendly explanation,
    /// and a list of source rule identifiers for auditability and deterministic unit tests.
    /// </summary>
    public interface IAuthorizationService
    {
        /// <summary>
        /// Evaluate whether the user is allowed to perform the specified action on an optional resource.
        /// - Deny-wins semantics are enforced by implementations (any matching Deny causes overall Deny).
        /// - Implementations should be deterministic and return ExplainText + Sources for observability.
        /// </summary>
        Task<AuthorizationResult> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default);

        /// <summary>
        /// Batch evaluation: return a decision per resource id. Implementations may optimize by reusing caches/queries.
        /// </summary>
        Task<IReadOnlyDictionary<long, AuthorizationResult>> IsAuthorizedBatchAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default);

        /// <summary>
        /// Invalidate any internal caches for the given user (used after role/permission changes or in tests).
        /// </summary>
        Task InvalidateUserCacheAsync(long userId);
    }

    /// <summary>
    /// Immutable result returned by authorization checks.
    /// - Allowed: true when final decision permits the action.
    /// - Effect: "Allow", "Deny", or "Audit" (Audit treated as non-allowing decision but recorded).
    /// - ExplainText: concise human-readable rationale for logging and test assertions.
    /// - Sources: ordered list of rule Ids that influenced the decision (useful for deterministic tests).
    /// </summary>
    public sealed record AuthorizationResult(bool Allowed, string Effect, string ExplainText, IReadOnlyList<string> Sources)
    {
        public static AuthorizationResult Allow(string explain, params string[] sources) =>
            new(true, "Allow", explain ?? string.Empty, sources ?? new string[0]);

        public static AuthorizationResult Deny(string explain, params string[] sources) =>
            new(false, "Deny", explain ?? string.Empty, sources ?? new string[0]);

        public static AuthorizationResult Audit(string explain, params string[] sources) =>
            new(false, "Audit", explain ?? string.Empty, sources ?? new string[0]);
    }
}
