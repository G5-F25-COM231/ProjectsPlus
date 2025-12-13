// src/Data/Configurations/CommsEntityConfigurations.cs
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

// adjust if your entities live elsewhere

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter
{
    public class DbKeyValueEntityConfiguration : IEntityTypeConfiguration<KeyValueEntity>
    {
        public void Configure(EntityTypeBuilder<KeyValueEntity> b)
        {
            b.ToTable("KeyValues");

            b.HasKey(e => e.Id);
            b.Property(e => e.Id)
             .HasMaxLength(200)
             .IsRequired();

            b.Property(e => e.Value)
             .HasColumnType("nvarchar(max)")
             .IsRequired();

            b.Property(e => e.UpdatedAtUtc)
             .IsRequired();

            b.HasIndex(e => e.UpdatedAtUtc);
        }
    }

    public class DbSetEntityConfiguration : IEntityTypeConfiguration<SetEntity>
    {
        public void Configure(EntityTypeBuilder<SetEntity> b)
        {
            b.ToTable("Sets");

            b.HasKey(e => e.Id);
            b.Property(e => e.Id)
             .HasMaxLength(200)
             .IsRequired();

            b.Property(e => e.MembersJson)
             .HasColumnType("nvarchar(max)")
             .IsRequired(false);

            b.Property(e => e.UpdatedAtUtc)
             .IsRequired();

            // Optional: index on UpdatedAtUtc to help cleanup/retention queries
            b.HasIndex(e => e.UpdatedAtUtc);
        }
    }

    public class DbPubSubMessageEntityConfiguration : IEntityTypeConfiguration<PubSubMessageEntity>
    {
        public void Configure(EntityTypeBuilder<PubSubMessageEntity> b)
        {
            b.ToTable("PubSubMessages");

            b.HasKey(e => e.Id);

            b.Property(e => e.Id)
             .IsRequired();

            b.Property(e => e.Channel)
             .HasMaxLength(200)
             .IsRequired();

            b.Property(e => e.Payload)
             .HasColumnType("nvarchar(max)")
             .IsRequired();

            b.Property(e => e.CreatedAtUtc)
             .IsRequired();

            // Composite index to support efficient polling by channel and time
            b.HasIndex(e => new { e.Channel, e.CreatedAtUtc });
        }
    }
}
