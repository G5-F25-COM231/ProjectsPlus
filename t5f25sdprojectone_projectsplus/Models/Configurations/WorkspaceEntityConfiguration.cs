using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Data.EntityConfigurations
{
    public class WorkspaceEntityConfiguration : IEntityTypeConfiguration<WorkspaceEntity>
    {
        public void Configure(EntityTypeBuilder<WorkspaceEntity> builder)
        {
            builder.ToTable("Workspaces");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId)
                .IsRequired();

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.State)
                .HasConversion<int>()
                .HasDefaultValue(WorkspaceState.ProvisioningPending);

            builder.Property(x => x.MetadataJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.OwnerUserId)
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

            // Unique constraint: slug scoped to project (unique per project)
            builder.HasIndex(x => new { x.ProjectId, x.Slug }).IsUnique();

            builder.HasIndex(x => x.ProjectId);
            builder.HasIndex(x => x.OwnerUserId);

            // Note: MetadataJson should not contain secrets; service-layer must enforce secretRef usage.
        }
    }
}
