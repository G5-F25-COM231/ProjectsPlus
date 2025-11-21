// src/Models/Authorization/UserRole.cs
namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    public class UserRole
    {
        public long UserId { get; set; }
        public long RoleId { get; set; }

        public t5f25sdprojectone_projectsplus.Models.Users.UserEntity? User { get; set; }
        public Role? Role { get; set; }
    }
}
