// Configurations/Boards/KanbanColumnEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    public class KanbanColumnEntityTypeConfiguration : IEntityTypeConfiguration<KanbanColumn>
    {
        public void Configure(EntityTypeBuilder<KanbanColumn> builder)
        {
            builder.ToTable("kanban_columns");

            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanColumn.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_column_system_type_id_system_types");

            builder.Property(c => c.BoardId).HasColumnName("board_id").IsRequired();

            builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            builder.Property(c => c.Slug).HasColumnName("slug").HasMaxLength(200).IsRequired();
            builder.Property(c => c.Position).HasColumnName("position").IsRequired();
            builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(4000).IsRequired(false);
            builder.Property(c => c.IsArchived).HasColumnName("is_archived").IsRequired();
            builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(c => c.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            // Unique slug per board
            builder.HasIndex(c => new { c.BoardId, c.Slug })
                   .IsUnique()
                   .HasDatabaseName("ux_kanban_columns_board_id_slug");

            builder.HasIndex(c => c.BoardId).HasDatabaseName("ix_kanban_columns_board_id");
            builder.HasIndex(c => c.Position).HasDatabaseName("ix_kanban_columns_position");

            // FK to kanban_boards
            builder.HasOne<KanbanBoard>()
                   .WithMany(b => b.Columns)
                   .HasForeignKey(nameof(KanbanColumn.BoardId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_columns_board_id_kanban_boards");
        }
    }
}
