// Configurations/Storage/FileRecordEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Storage
{
    public class FileRecordEntityTypeConfiguration : IEntityTypeConfiguration<FileRecord>
    {
        public void Configure(EntityTypeBuilder<FileRecord> builder)
        {
            builder.ToTable("file_records");

            builder.HasKey(f => f.Id);
            builder.Property(f => f.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(FileRecord.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_file_records_system_type_id_system_types");


            builder.Property(f => f.StoragePath).HasColumnName("storage_path").HasMaxLength(2000).IsRequired();
            builder.Property(f => f.Filename).HasColumnName("filename").HasMaxLength(1000).IsRequired();
            builder.Property(f => f.ContentType).HasColumnName("content_type").HasMaxLength(200).IsRequired();
            builder.Property(f => f.SizeBytes).HasColumnName("size_bytes").IsRequired();
            builder.Property(f => f.Checksum).HasColumnName("checksum").HasMaxLength(128).IsRequired();
            builder.Property(f => f.Status).HasColumnName("status").IsRequired();
            builder.Property(f => f.ProfileJson).HasColumnName("profile_json").HasMaxLength(8000).IsRequired(false);
            builder.Property(f => f.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
            builder.Property(f => f.CorrelationId).HasColumnName("correlation_id").HasMaxLength(200).IsRequired(false);
            builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(f => f.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(f => f.Checksum).HasDatabaseName("ix_file_records_checksum");
            builder.HasIndex(f => f.Status).HasDatabaseName("ix_file_records_status");
            builder.HasIndex(f => f.CreatedByUserId).HasDatabaseName("ix_file_records_created_by");
        }
    }
}
