// src/Data/EntityConfigurations/UserRoleConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Authorization
{
    public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
    {
        public void Configure(EntityTypeBuilder<UserRole> builder)
        {
            builder.ToTable("user_roles");
            builder.HasKey(ur => new { ur.UserId, ur.RoleId });

            builder.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
            builder.HasOne(ur => ur.User).WithMany().HasForeignKey(ur => ur.UserId);
        }
    }
}
