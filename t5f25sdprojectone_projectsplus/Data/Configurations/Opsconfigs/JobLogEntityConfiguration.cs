using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Jobs;

namespace t5f25sdprojectone_projectsplus.Data.Configurations.Opsconfigs
{
    public class JobLogEntityConfiguration : IEntityTypeConfiguration<JobLogEntity>
    {
        public void Configure(EntityTypeBuilder<JobLogEntity> builder)
        {
            builder.ToTable("JobLogs");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.JobId)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.JobType)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.ScopeKey)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(x => x.CorrelationId)
                .IsRequired();

            builder.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(x => x.Attempts)
                .IsRequired();

            builder.Property(x => x.StartedAt)
                .HasColumnType("datetimeoffset");

            builder.Property(x => x.FinishedAt)
                .HasColumnType("datetimeoffset");

            builder.Property(x => x.OutcomeJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.OutcomeCode)
                .HasMaxLength(200);

            builder.Property(x => x.Owner)
                .HasMaxLength(200);

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

            // Enforce uniqueness per-scope: UniqueKey semantics (ScopeKey + CorrelationId)
            builder.HasIndex(x => new { x.ScopeKey, x.CorrelationId }).IsUnique();

            builder.HasIndex(x => x.JobId);
            builder.HasIndex(x => x.JobType);
        }
    }
}
