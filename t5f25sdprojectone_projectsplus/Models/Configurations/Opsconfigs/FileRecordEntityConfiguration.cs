using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Opsconfigs
{
    public class FileRecordEntityConfiguration : IEntityTypeConfiguration<FileRecordEntity>
    {
        public void Configure(EntityTypeBuilder<FileRecordEntity> builder)
        {
            builder.ToTable("FileRecords");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.OwnerUserId).IsRequired();
            builder.Property(x => x.ProjectId);
            builder.Property(x => x.SystemTypeId).IsRequired();

            builder.Property(x => x.StorageKey)
                .IsRequired()
                .HasMaxLength(2000);

            builder.Property(x => x.ChecksumSha256)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(x => x.OriginalFileName)
                .HasMaxLength(1024);

            builder.Property(x => x.MimeType)
                .HasMaxLength(256);

            builder.Property(x => x.SizeBytes)
                .IsRequired();

            builder.Property(x => x.ScanStatus)
                .HasConversion<int>()
                .HasDefaultValue(ScanStatus.Pending);

            builder.Property(x => x.QuarantineReason)
                .HasMaxLength(2000);

            builder.Property(x => x.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(1);

            builder.Property(x => x.IsDeleted)
                .HasDefaultValue(false);

            builder.Property(x => x.CreatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            // Uniqueness: prevent duplicate storageKey; also index checksum + owner for dedupe
            builder.HasIndex(x => x.StorageKey).IsUnique();
            builder.HasIndex(x => new { x.ChecksumSha256, x.OwnerUserId });

            // Consider limiting StorageKey length for provider compatibility; 2k chosen as conservative.
        }
    }
}
