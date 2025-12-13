// Configurations/Boards/KanbanBoardEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.Models.Workspace.Group;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    public class KanbanBoardEntityTypeConfiguration : IEntityTypeConfiguration<KanbanBoard>
    {
        public void Configure(EntityTypeBuilder<KanbanBoard> builder)
        {
            builder.ToTable("kanban_boards");

            builder.HasKey(b => b.Id);
            builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanBoard.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_board_system_type_id_system_types");

            builder.Property(b => b.WorkspaceId).HasColumnName("workspace_id").IsRequired();
            builder.Property(b => b.ProjectId).HasColumnName("project_id").IsRequired(false);

            builder.Property(b => b.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            builder.Property(b => b.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
            builder.Property(b => b.Description).HasColumnName("description").HasMaxLength(4000).IsRequired(false);
            builder.Property(b => b.IsArchived).HasColumnName("is_archived").IsRequired();
            builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(b => b.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            // Unique per scope: workspace + project (nullable) + slug
            builder.HasIndex(b => new { b.WorkspaceId, b.ProjectId, b.Slug })
                   .IsUnique()
                   .HasDatabaseName("ux_kanban_boards_workspace_project_slug");

            builder.HasIndex(b => b.WorkspaceId).HasDatabaseName("ix_kanban_boards_workspace_id");
            builder.HasIndex(b => b.ProjectId).HasDatabaseName("ix_kanban_boards_project_id");

            // FK to workspace
            builder.HasOne<Workspace>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanBoard.WorkspaceId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_boards_workspace_id_workspaces");

            // Optional FK to project
            builder.HasOne<Project.Project>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanBoard.ProjectId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_boards_project_id_projects");
        }
    }
}
