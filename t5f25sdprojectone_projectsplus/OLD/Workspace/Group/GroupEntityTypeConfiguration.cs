// Configurations/Workspace/GroupEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.Group
{
    /// <summary>
    /// EF Core configuration for Group.
    /// - Table "groups"
    /// - Unique(workspace_id, slug)
    /// - FK workspace_id -> workspaces.id with Restrict
    /// - Version as concurrency token
    /// </summary>
    public class GroupEntityTypeConfiguration : IEntityTypeConfiguration<Group>
    {
        public void Configure(EntityTypeBuilder<Group> builder)
        {
            builder.ToTable("groups");

            builder.HasKey(g => g.Id);
            builder.Property(g => g.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(Group.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_group_system_type_id_system_types");


            builder.Property(g => g.WorkspaceId)
                   .HasColumnName("workspace_id")
                   .IsRequired();

            builder.Property(g => g.Name)
                   .HasColumnName("name")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(g => g.Slug)
                   .HasColumnName("slug")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(g => g.Description)
                   .HasColumnName("description")
                   .HasMaxLength(4000)
                   .IsRequired(false);

            builder.Property(g => g.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(g => g.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(g => g.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(g => new { g.WorkspaceId, g.Slug })
                   .IsUnique()
                   .HasDatabaseName("ux_groups_workspace_id_slug");

            builder.HasIndex(g => g.WorkspaceId)
                   .HasDatabaseName("ix_groups_workspace_id");

            builder.HasOne<Workspace>()
                   .WithMany()
                   .HasForeignKey(nameof(Group.WorkspaceId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_groups_workspace_id_workspaces");
        }
    }
}
