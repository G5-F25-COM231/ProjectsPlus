// src/Models/Authorization/RolePermission.cs
namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    public class RolePermission
    {
        public long RoleId { get; set; }
        public long PermissionId { get; set; }

        public Role? Role { get; set; }
        public Permission? Permission { get; set; }
    }
}

