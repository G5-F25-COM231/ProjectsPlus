// src/Models/Authorization/Role.cs
using System;
using System.Collections.Generic;

namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    public class Role
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}
