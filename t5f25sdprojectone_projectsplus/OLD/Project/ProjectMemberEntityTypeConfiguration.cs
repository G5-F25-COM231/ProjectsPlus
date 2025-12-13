// Configurations/Project/ProjectMemberEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    /// <summary>
    /// EF Core configuration for ProjectMember.
    /// - Table "project_members"
    /// - Unique(project_id, user_id)
    /// - Index on project_id and role for queries
    /// - FKs to projects and users with DeleteBehavior.Restrict
    /// - Version as concurrency token
    /// </summary>
    public class ProjectMemberEntityTypeConfiguration : IEntityTypeConfiguration<ProjectMember>
    {
        public void Configure(EntityTypeBuilder<ProjectMember> builder)
        {
            builder.ToTable("project_members");

            builder.HasKey(pm => pm.Id);
            builder.Property(pm => pm.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(ProjectMember.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_member_system_type_id_system_types");

            builder.Property(pm => pm.ProjectId)
                   .HasColumnName("project_id")
                   .IsRequired();

            builder.Property(pm => pm.UserId)
                   .HasColumnName("user_id")
                   .IsRequired();

            builder.Property(pm => pm.Role)
                   .HasColumnName("role")
                   .IsRequired();

            builder.Property(pm => pm.JoinedAt)
                   .HasColumnName("joined_at")
                   .IsRequired(false);

            builder.Property(pm => pm.RemovedAt)
                   .HasColumnName("removed_at")
                   .IsRequired(false);

            builder.Property(pm => pm.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(pm => pm.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(pm => pm.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(pm => new { pm.ProjectId, pm.UserId })
                   .IsUnique()
                   .HasDatabaseName("ux_project_members_project_id_user_id");

            builder.HasIndex(pm => pm.ProjectId)
                   .HasDatabaseName("ix_project_members_project_id");

            builder.HasIndex(pm => pm.Role)
                   .HasDatabaseName("ix_project_members_role");

            // FK to projects
            builder.HasOne<Project>()
                   .WithMany(p => p.Members)
                   .HasForeignKey(nameof(ProjectMember.ProjectId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_members_project_id_projects");

            // FK to users
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(ProjectMember.UserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_members_user_id_users");
        }
    }
}
