// src/Services/Authorization/AuthorizationOptions.cs
namespace t5f25sdprojectone_projectsplus.Services.Authorization
{
    public class AuthorizationOptions
    {
        // Admin role name; users in this role bypass permission checks
        public string AdminRoleName { get; set; } = "Admin";

        // Cache duration seconds for user-permission mapping
        public int CacheDurationSeconds { get; set; } = 60;
    }
}
