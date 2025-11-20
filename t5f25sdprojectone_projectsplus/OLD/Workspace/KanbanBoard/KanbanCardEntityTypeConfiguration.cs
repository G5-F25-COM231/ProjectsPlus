// Configurations/Boards/KanbanCardEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    public class KanbanCardEntityTypeConfiguration : IEntityTypeConfiguration<KanbanCard>
    {
        public void Configure(EntityTypeBuilder<KanbanCard> builder)
        {
            builder.ToTable("kanban_cards");

            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanCard.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_system_type_id_system_types");

            builder.Property(c => c.ColumnId).HasColumnName("column_id").IsRequired();
            builder.Property(c => c.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            builder.Property(c => c.Description).HasColumnName("description").HasMaxLength(8000).IsRequired(false);
            builder.Property(c => c.Position).HasColumnName("position").IsRequired();
            builder.Property(c => c.IsBlocked).HasColumnName("is_blocked").IsRequired();
            builder.Property(c => c.IsArchived).HasColumnName("is_archived").IsRequired();
            builder.Property(c => c.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(c => c.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(c => new { c.ColumnId, c.Position })
                   .HasDatabaseName("ix_kanban_cards_column_id_position");

            builder.HasIndex(c => c.IsArchived).HasDatabaseName("ix_kanban_cards_is_archived");

            builder.HasOne<KanbanColumn>()
                   .WithMany(col => col.Cards)
                   .HasForeignKey(nameof(KanbanCard.ColumnId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_cards_column_id_kanban_columns");
        }
    }

    public class KanbanCardAttachmentEntityTypeConfiguration : IEntityTypeConfiguration<KanbanCardAttachment>
    {
        public void Configure(EntityTypeBuilder<KanbanCardAttachment> builder)
        {
            builder.ToTable("kanban_card_attachments");

            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanCardAttachment.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_attachment_system_type_id_system_types");

            builder.Property(a => a.CardId).HasColumnName("card_id").IsRequired();
            builder.Property(a => a.FileRecordId).HasColumnName("file_record_id").IsRequired();
            builder.Property(a => a.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(a => a.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            builder.Property(a => a.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(a => a.CardId).HasDatabaseName("ix_kanban_card_attachments_card_id");
            builder.HasIndex(a => a.FileRecordId).HasDatabaseName("ix_kanban_card_attachments_file_record_id");

            builder.HasOne<KanbanCard>()
                   .WithMany(c => c.Attachments)
                   .HasForeignKey(nameof(KanbanCardAttachment.CardId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_attachments_card_id_kanban_cards");
        }
    }

    public class KanbanCardAssigneeEntityTypeConfiguration : IEntityTypeConfiguration<KanbanCardAssignee>
    {
        public void Configure(EntityTypeBuilder<KanbanCardAssignee> builder)
        {
            builder.ToTable("kanban_card_assignees");

            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanCardAssignee.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_assignee_system_type_id_system_types");

            builder.Property(a => a.CardId).HasColumnName("card_id").IsRequired();
            builder.Property(a => a.ProjectMemberId).HasColumnName("project_member_id").IsRequired();
            builder.Property(a => a.AssignedAt).HasColumnName("assigned_at").IsRequired();
            builder.Property(a => a.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            builder.Property(a => a.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(a => a.CardId).HasDatabaseName("ix_kanban_card_assignees_card_id");
            builder.HasIndex(a => a.ProjectMemberId).HasDatabaseName("ix_kanban_card_assignees_project_member_id");

            builder.HasOne<KanbanCard>()
                   .WithMany(c => c.Assignees)
                   .HasForeignKey(nameof(KanbanCardAssignee.CardId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_assignees_card_id_kanban_cards");

            // FK to project_members is intentionally modeled as bare FK name; configure if ProjectMember entity available in same DbContext
            builder.HasOne<Project.ProjectMember>()
                   .WithMany()
                   .HasForeignKey(nameof(KanbanCardAssignee.ProjectMemberId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_kanban_card_assignees_project_member_id_project_members");
        }
    }
}
