using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{

    /// <summary>
    /// Repository surface for writing audit records.
    /// This minimal contract includes the authorization-audit helper used by controllers and services.
    /// Implementations should persist records with actor (nullable), action, outcome and an optional reason/payload.
    /// </summary>
    public interface IAuditRepository
    {
        Task WriteAsync(ProjectAudit audit, CancellationToken ct = default);

        /// <summary>
        /// Record an authorization-related audit entry.
        /// - actorUserId may be null for anonymous attempts
        /// - action is a short name like "auth.login" or "project.delete"
        /// - outcome is typically "allow" or "deny"
        /// - detail may contain a brief reason, correlation id, or payload
        /// </summary>
        Task RecordAuthorizationAuditAsync(long? actorUserId, string action, string outcome, string? detail = null, CancellationToken ct = default);
    }
}
