// Configurations/Project/ProjectEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    /// <summary>
    /// EF Core configuration for Project.
    /// - Table "projects"
    /// - Unique(workspace_id, slug)
    /// - FKs: workspace_id -> workspaces.id, owner_user_id -> users.id
    /// - Optional FK github_link_id -> github_project_links.id (Restrict)
    /// - Version as concurrency token
    /// </summary>
    public class ProjectEntityTypeConfiguration : IEntityTypeConfiguration<Project>
    {
        public void Configure(EntityTypeBuilder<Project> builder)
        {
            builder.ToTable("projects");

            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(Project.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_system_type_id_system_types");

            builder.Property(p => p.WorkspaceId)
                   .HasColumnName("workspace_id")
                   .IsRequired();

            builder.Property(p => p.Name)
                   .HasColumnName("name")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(p => p.Slug)
                   .HasColumnName("slug")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(p => p.OwnerUserId)
                   .HasColumnName("owner_user_id")
                   .IsRequired();

            builder.Property(p => p.Status)
                   .HasColumnName("status")
                   .IsRequired()
                   .HasDefaultValue(0);

            builder.Property(p => p.Visibility)
                   .HasColumnName("visibility")
                   .IsRequired()
                   .HasDefaultValue(0);

            builder.Property(p => p.GithubLinkId)
                   .HasColumnName("github_link_id")
                   .IsRequired(false);

            builder.Property(p => p.Description)
                   .HasColumnName("description")
                   .HasMaxLength(8000)
                   .IsRequired(false);

            builder.Property(p => p.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(p => p.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(p => p.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(p => new { p.WorkspaceId, p.Slug })
                   .IsUnique()
                   .HasDatabaseName("ux_projects_workspace_id_slug");

            builder.HasIndex(p => p.OwnerUserId)
                   .HasDatabaseName("ix_projects_owner_user_id");

            // FK to workspaces
            builder.HasOne<Workspace.Workspace>()
                   .WithMany()
                   .HasForeignKey(nameof(Project.WorkspaceId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_projects_workspace_id_workspaces");

            // FK to users (owner)
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(Project.OwnerUserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_projects_owner_user_id_users");

            // Optional FK to github_project_links
            builder.HasOne<GitHubProjectLink>()
                   .WithMany()
                   .HasForeignKey(nameof(Project.GithubLinkId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_projects_github_link_id_github_project_links");
        }
    }
}
