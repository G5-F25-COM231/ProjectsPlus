using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Project
{
    public class ProjectEntityConfiguration : IEntityTypeConfiguration<ProjectEntity>
    {
        public void Configure(EntityTypeBuilder<ProjectEntity> builder)
        {
            builder.ToTable("Projects");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Title)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(x => x.ShortDescription)
                .HasMaxLength(1024);

            builder.Property(x => x.LongDescription)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.AdditionCompatibilityJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.RequiresBaseConsent)
                .HasDefaultValue(false);

            builder.Property(x => x.IsDeleted)
                .HasDefaultValue(false);

            builder.Property(x => x.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(1);

            builder.Property(x => x.CreatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            builder.HasIndex(x => x.OwnerUserId);
            builder.HasIndex(x => x.Status);

            // Note: no global unique constraint on title by default; if required,
            // scope it to ownerUserId or add a separate business rule.

            // No data seed here — seeds for projects are created via seed scripts or service-level seeds.
            // Keep configuration focused on schema.
        }
    }
}
