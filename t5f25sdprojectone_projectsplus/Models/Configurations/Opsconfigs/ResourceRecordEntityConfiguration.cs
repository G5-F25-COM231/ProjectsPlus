using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Opsconfigs
{
    public class ResourceRecordEntityConfiguration : IEntityTypeConfiguration<ResourceRecordEntity>
    {
        public void Configure(EntityTypeBuilder<ResourceRecordEntity> builder)
        {
            builder.ToTable("ResourceRecords");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Provider)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.ProviderResourceId)
                .IsRequired()
                .HasMaxLength(1000);

            builder.Property(x => x.ProfileJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.CorrelationId)
                .IsRequired();

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

            builder.HasIndex(x => new { x.Provider, x.ProviderResourceId })
                .IsUnique();

            builder.HasIndex(x => x.ProjectId);
            builder.HasIndex(x => x.WorkspaceId);

            // SystemTypeId is an integer reference to a seeded SystemType table.
            builder.Property(x => x.SystemTypeId)
                .HasDefaultValue(0)
                .IsRequired();

            // Ensure ProfileJson does not accidentally store secrets is a service-level responsibility.
            // This configuration focuses on schema, indexes, and constraints.
        }
    }
}
