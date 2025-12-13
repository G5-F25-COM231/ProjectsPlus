// src/Models/Authorization/Permission.cs
using System;

namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    public class Permission
    {
        public long Id { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
