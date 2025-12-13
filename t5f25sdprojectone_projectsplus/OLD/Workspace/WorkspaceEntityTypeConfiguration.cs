// Configurations/Workspace/WorkspaceEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.Models.Workspace.KanbanBoard;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace
{
    /// <summary>
    /// EF Core configuration for Workspace.
    /// - Table "workspaces"
    /// - Unique index on slug
    /// - FK owner_user_id -> users.id with DeleteBehavior.Restrict
    /// - HasMany Projects configured with Restrict delete behavior from Project side
    /// - Version is concurrency token
    /// </summary>
    public class WorkspaceEntityTypeConfiguration : IEntityTypeConfiguration<Workspace>
    {
        public void Configure(EntityTypeBuilder<Workspace> builder)
        {
            builder.ToTable("workspaces");

            builder.HasKey(w => w.Id);
            builder.Property(w => w.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(Workspace.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_workspace_system_type_id_system_types");

            builder.Property(w => w.Name)
                   .HasColumnName("name")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(w => w.Slug)
                   .HasColumnName("slug")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(w => w.OwnerUserId)
                   .HasColumnName("owner_user_id")
                   .IsRequired();

            builder.Property(w => w.DefaultVisibility)
                   .HasColumnName("default_visibility")
                   .IsRequired()
                   .HasDefaultValue(0);

            builder.Property(w => w.Description)
                   .HasColumnName("description")
                   .HasMaxLength(4000)
                   .IsRequired(false);

            builder.Property(w => w.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(w => w.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(w => w.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(w => w.Slug)
                   .IsUnique()
                   .HasDatabaseName("ix_workspaces_slug");

            // FK to users table; do not cascade delete owner removal.
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(Workspace.OwnerUserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_workspaces_owner_user_id_users");

            // Relationship: Workspace 1..* Projects
            builder.HasMany(w => w.Projects)
                   .WithOne(p => p.Workspace)
                   .HasForeignKey(p => p.WorkspaceId)
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_projects_workspace_id_workspaces");
        }
    }
}
