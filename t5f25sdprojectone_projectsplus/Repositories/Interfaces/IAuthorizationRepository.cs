// src/Repositories/Interfaces/IAuthorizationRepository.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Authorization;

namespace t5f25sdprojectone_projectsplus.Repositories.Interfaces
{
    public interface IAuthorizationRepository
    {
        // Read operations (optimized for permission checks)
        Task<IReadOnlyList<string>> GetPermissionsForUserAsync(long userId, CancellationToken ct = default);
        Task<bool> UserHasRoleAsync(long userId, string roleName, CancellationToken ct = default);

        // Lookup helpers
        Task<Role?> FindRoleByNameAsync(string roleName, CancellationToken ct = default);
        Task<Permission?> FindPermissionByNameAsync(string permissionName, CancellationToken ct = default);

        // Management operations - expected to be idempotent
        Task<Role> CreateRoleAsync(Role role, CancellationToken ct = default);
        Task<Permission> CreatePermissionAsync(Permission permission, CancellationToken ct = default);
        Task AssignRoleToUserAsync(long userId, long roleId, CancellationToken ct = default);
        Task AssignPermissionToRoleAsync(long roleId, long permissionId, CancellationToken ct = default);

        // Optional administrative queries
        Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default);
        Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken ct = default);
    }
}
