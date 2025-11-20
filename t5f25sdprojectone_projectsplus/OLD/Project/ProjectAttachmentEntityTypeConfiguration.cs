// Configurations/Project/ProjectAttachmentEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.OLD.Storage;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    public class ProjectAttachmentEntityTypeConfiguration : IEntityTypeConfiguration<ProjectAttachment>
    {
        public void Configure(EntityTypeBuilder<ProjectAttachment> builder)
        {
            builder.ToTable("project_attachments");

            builder.HasKey(pa => pa.Id);
            builder.Property(pa => pa.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(ProjectAttachment.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_attachment_system_type_id_system_types");

            builder.Property(pa => pa.ProjectId).HasColumnName("project_id").IsRequired();
            builder.Property(pa => pa.FileRecordId).HasColumnName("file_record_id").IsRequired();
            builder.Property(pa => pa.Role).HasColumnName("role").HasMaxLength(200).IsRequired();
            builder.Property(pa => pa.UploadedForProjectStatus).HasColumnName("uploaded_for_project_status").IsRequired(false);
            builder.Property(pa => pa.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            builder.Property(pa => pa.CorrelationId).HasColumnName("correlation_id").HasMaxLength(200).IsRequired(false);
            builder.Property(pa => pa.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(pa => pa.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(pa => pa.ProjectId).HasDatabaseName("ix_project_attachments_project_id");
            builder.HasIndex(pa => pa.FileRecordId).HasDatabaseName("ix_project_attachments_file_record_id");
            builder.HasIndex(pa => new { pa.ProjectId, pa.FileRecordId, pa.Role }).HasDatabaseName("ux_project_attachments_project_file_role");

            // FKs
            builder.HasOne<Project>()
                   .WithMany(p => p.Attachments)
                   .HasForeignKey(nameof(ProjectAttachment.ProjectId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_attachments_project_id_projects");

            builder.HasOne<FileRecord>()
                   .WithMany()
                   .HasForeignKey(nameof(ProjectAttachment.FileRecordId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_project_attachments_file_record_id_file_records");
        }
    }
}
