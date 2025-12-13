// src/Repositories/EF/AuthorizationRepository.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class AuthorizationRepository : IAuthorizationRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public AuthorizationRepository(ProjectsPlusDbContext db) => _db = db ?? throw new ArgumentNullException(nameof(db));

        public async Task<IReadOnlyList<string>> GetPermissionsForUserAsync(long userId, CancellationToken ct = default)
        {
            if (userId <= 0) return Array.Empty<string>();

            var permissions = await (from ur in _db.UserRoles.AsNoTracking()
                                     join rp in _db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
                                     join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
                                     where ur.UserId == userId
                                     select p.Name)
                                     .Distinct()
                                     .ToListAsync(ct).ConfigureAwait(false);

            return permissions;
        }

        public async Task<bool> UserHasRoleAsync(long userId, string roleName, CancellationToken ct = default)
        {
            if (userId <= 0 || string.IsNullOrWhiteSpace(roleName)) return false;

            return await _db.UserRoles
                .AsNoTracking()
                .Include(ur => ur.Role)
                .Where(ur => ur.UserId == userId && ur.Role!.Name == roleName)
                .AnyAsync(ct)
                .ConfigureAwait(false);
        }

        public Task<Role?> FindRoleByNameAsync(string roleName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return Task.FromResult<Role?>(null);
            return _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == roleName, ct);
        }

        public Task<Permission?> FindPermissionByNameAsync(string permissionName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(permissionName)) return Task.FromResult<Permission?>(null);
            return _db.Permissions.AsNoTracking().FirstOrDefaultAsync(p => p.Name == permissionName, ct);
        }

        public async Task<Role> CreateRoleAsync(Role role, CancellationToken ct = default)
        {
            _db.Roles.Add(role);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return role;
        }

        public async Task<Permission> CreatePermissionAsync(Permission permission, CancellationToken ct = default)
        {
            _db.Permissions.Add(permission);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return permission;
        }

        public async Task AssignRoleToUserAsync(long userId, long roleId, CancellationToken ct = default)
        {
            var exists = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct).ConfigureAwait(false);
            if (exists) return;

            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task AssignPermissionToRoleAsync(long roleId, long permissionId, CancellationToken ct = default)
        {
            var exists = await _db.RolePermissions.AnyAsync(rp => rp.RoleId == roleId && rp.PermissionId == permissionId, ct).ConfigureAwait(false);
            if (exists) return;

            _db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default)
            => _db.Roles.AsNoTracking().ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Role>)t.Result, TaskScheduler.Default);

        public Task<IReadOnlyList<Permission>> ListPermissionsAsync(CancellationToken ct = default)
            => _db.Permissions.AsNoTracking().ToListAsync(ct).ContinueWith(t => (IReadOnlyList<Permission>)t.Result, TaskScheduler.Default);
    }
}
