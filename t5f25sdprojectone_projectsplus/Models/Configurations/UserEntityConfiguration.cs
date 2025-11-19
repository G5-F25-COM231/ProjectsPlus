using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Models.Configurations
{
    public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(x => x.DisplayName)
                .IsRequired()
                .HasMaxLength(256);

            builder.Property(x => x.ProviderId)
                .HasMaxLength(256);

            builder.Property(x => x.AttributesJson)
                .HasColumnType("nvarchar(max)");

            builder.Property(x => x.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(1);

            builder.Property(x => x.IsDeleted)
                .HasDefaultValue(false);

            builder.Property(x => x.IsActive)
                .HasDefaultValue(true);

            builder.Property(x => x.CreatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            builder.Property(x => x.UpdatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired();

            builder.HasIndex(x => x.Email)
                .IsUnique();

            // Stable, deterministic seed data for initial system users.
            // NOTE: HasData requires constant values; timestamps are set to a fixed UTC value.
            var seedTime = new DateTimeOffset(2025, 11, 19, 02, 00, 00, TimeSpan.Zero);

            builder.HasData(
                new UserEntity
                {
                    Id = 1000,
                    Email = "admin@example.edu",
                    DisplayName = "System Admin",
                    ProviderId = null,
                    AttributesJson = null,
                    Version = 1,
                    CreatedAt = seedTime,
                    UpdatedAt = seedTime,
                    IsDeleted = false,
                    IsActive = true
                },
                new UserEntity
                {
                    Id = 1001,
                    Email = "faculty@example.edu",
                    DisplayName = "Faculty User",
                    ProviderId = null,
                    AttributesJson = null,
                    Version = 1,
                    CreatedAt = seedTime,
                    UpdatedAt = seedTime,
                    IsDeleted = false,
                    IsActive = true
                },
                new UserEntity
                {
                    Id = 1002,
                    Email = "student@example.edu",
                    DisplayName = "Student User",
                    ProviderId = null,
                    AttributesJson = null,
                    Version = 1,
                    CreatedAt = seedTime,
                    UpdatedAt = seedTime,
                    IsDeleted = false,
                    IsActive = true
                },
                new UserEntity
                {
                    Id = 1003,
                    Email = "external@example.edu",
                    DisplayName = "External User",
                    ProviderId = null,
                    AttributesJson = null,
                    Version = 1,
                    CreatedAt = seedTime,
                    UpdatedAt = seedTime,
                    IsDeleted = false,
                    IsActive = true
                }
            );
        }
    }
}
