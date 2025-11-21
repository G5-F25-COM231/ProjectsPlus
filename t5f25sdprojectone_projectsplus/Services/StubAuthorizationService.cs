// src/Services/Authorization/StubAuthorizationService.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// Permissive stub for Phase 4 wiring. Returns allow for all checks and provides simple batch helpers.
    /// Replace with real policy-backed implementation in Phase 5.
    /// </summary>
    public class StubAuthorizationService : IAuthorizationService
    {
        public Task<bool> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CanViewProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.View", "Project", projectId, ct);

        public Task<bool> CanEditProjectAsync(long userId, long projectId, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, "Project.Edit", "Project", projectId, ct);

        public Task<bool> CanPerformGlobalActionAsync(long userId, string action, CancellationToken ct = default)
            => IsAuthorizedAsync(userId, action, null, null, ct);

        public Task<bool> IsAuthenticatedAsync(long userId, CancellationToken ct = default)
            => Task.FromResult(userId > 0);

        public Task<IReadOnlyDictionary<long, bool>> IsAuthorizedForManyAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default)
        {
            var dict = new Dictionary<long, bool>();
            foreach (var id in resourceIds) dict[id] = true;
            return Task.FromResult((IReadOnlyDictionary<long, bool>)dict);
        }
    }
}
