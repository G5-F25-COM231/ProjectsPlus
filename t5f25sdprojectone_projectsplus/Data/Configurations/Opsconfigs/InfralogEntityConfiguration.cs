using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models;

namespace t5f25sdprojectone_projectsplus.Data.Configurations.Opsconfigs
{
    public class InfralogEntityConfiguration : IEntityTypeConfiguration<InfralogEntity>
    {
        public void Configure(EntityTypeBuilder<InfralogEntity> builder)
        {
            builder.ToTable("Infralogs");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.CorrelationId)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.Category)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.Message)
                .IsRequired()
                .HasMaxLength(2000);

            builder.Property(x => x.DetailsJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.ActorUserId);

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

            builder.HasIndex(x => x.CorrelationId);
            builder.HasIndex(x => x.ActorUserId);
        }
    }
}
