// Services/Interfaces/IAuthorizationService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.Authorization.Interfaces
{
    /// <summary>
    /// Centralized authorization service contract used by controllers and services.
    /// Implementations must be side-effect free, fast, and deterministic for the same inputs.
    /// This contract expresses a single authoritative check plus a few convenience checks commonly used by controllers.
    /// </summary>
    public interface IAuthorizationService
    {
        /// <summary>
        /// Core authorization check.
        /// - <paramref name="userId"/> identifies the actor performing the action.
        /// - <paramref name="action"/> is a short, namespaced action string (examples: "Project.View", "Project.Edit", "Workspace.Create", "Admin.*").
        /// - <paramref name="resourceType"/> optionally identifies the domain resource type (examples: "Project", "Workspace").
        /// - <paramref name="resourceId"/> optionally identifies the resource instance.
        /// Returns true when the user is authorized to perform the action on the resource.
        /// </summary>
        Task<bool> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default);

        /// <summary>
        /// Convenience check: is the user authorized to view the given project.
        /// Implementations may call through to IsAuthorizedAsync("Project.View", "Project", projectId).
        /// </summary>
        Task<bool> CanViewProjectAsync(long userId, long projectId, CancellationToken ct = default);

        /// <summary>
        /// Convenience check: is the user authorized to edit the given project.
        /// Implementations may call through to IsAuthorizedAsync("Project.Edit", "Project", projectId).
        /// </summary>
        Task<bool> CanEditProjectAsync(long userId, long projectId, CancellationToken ct = default);

        /// <summary>
        /// Convenience check: is the user authorized to perform the named global action (no specific resource).
        /// Implementations may call through to IsAuthorizedAsync(action).
        /// </summary>
        Task<bool> CanPerformGlobalActionAsync(long userId, string action, CancellationToken ct = default);

        /// <summary>
        /// Convenience check: is the provided userId authenticated/active in the system.
        /// Implementations should return false for anonymous or disabled users.
        /// </summary>
        Task<bool> IsAuthenticatedAsync(long userId, CancellationToken ct = default);

        /// <summary>
        /// Optional bulk check helper: given a user, an action and multiple resource ids,
        /// return a map of resourceId => authorized (true/false). Useful to avoid N calls to IsAuthorizedAsync.
        /// Implementations may return an empty dictionary if bulk checks are not supported.
        /// </summary>
        Task<IReadOnlyDictionary<long, bool>> IsAuthorizedForManyAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default);
    }
}
