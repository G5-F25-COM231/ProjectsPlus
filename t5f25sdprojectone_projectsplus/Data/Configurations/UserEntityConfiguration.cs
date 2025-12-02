// src/ProjectsPlus.Data/Configurations/UserEntityConfiguration.cs
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.Data.Configurations
{
    public class UserEntityConfiguration : IEntityTypeConfiguration<UserEntity>
    {
        public void Configure(EntityTypeBuilder<UserEntity> builder)
        {
            builder.ToTable("Users");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Email)
                .IsRequired()
                .HasMaxLength(320)
                .HasColumnName("email");

            builder.Property(x => x.NormalizedEmail)
                .IsRequired()
                .HasMaxLength(320)
                .HasColumnName("normalized_email");

            builder.Property(x => x.DisplayName)
                .HasMaxLength(200)
                .HasColumnName("display_name");

            builder.Property(x => x.FirstName)
                .HasMaxLength(250)
                .HasColumnName("first_name");

            builder.Property(x => x.LastName)
                .HasMaxLength(250)
                .HasColumnName("last_name");

            builder.Property(x => x.ProviderId)
                .HasMaxLength(200)
                .HasColumnName("provider_id");

            builder.Property(x => x.PasswordHash)
                .HasMaxLength(2000)
                .HasColumnName("password_hash");

            builder.Property(x => x.PasswordSalt)
                .HasMaxLength(500)
                .HasColumnName("password_salt");

            builder.Property(x => x.SystemTypeId)
                .HasColumnName("system_type_id")
                .IsRequired(false);

            builder.Property(x => x.AttributesJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("attributes_json");

            builder.Property(x => x.ProfileJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("profile_json");

            builder.Property(x => x.IsDeleted)
                .IsRequired()
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");

            builder.Property(x => x.IsActive)
                .IsRequired()
                .HasDefaultValue(true)
                .HasColumnName("is_active");

            builder.Property(x => x.CreatedBy)
                .HasColumnName("created_by");

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .HasColumnType("datetimeoffset")
                .HasColumnName("created_at");

            builder.Property(x => x.UpdatedAt)
                .IsRequired()
                .HasColumnType("datetimeoffset")
                .HasColumnName("updated_at");

            builder.Property(x => x.Version)
                .IsRequired()
                .HasColumnName("version")
                .IsConcurrencyToken()
                .HasDefaultValue(1);

            // New communications / realtime columns
            builder.Property(x => x.LastSeenUtc)
                .HasColumnType("datetimeoffset")
                .HasColumnName("last_seen_utc")
                .IsRequired(false);

            builder.Property(x => x.PresenceStatus)
                .HasMaxLength(64)
                .HasColumnName("presence_status")
                .IsRequired(false);

            builder.Property(x => x.ActiveConnectionIdsJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("active_connection_ids_json")
                .IsRequired(false);

            builder.Property(x => x.NotificationPreferencesJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("notification_preferences_json")
                .IsRequired(false);

            builder.Property(x => x.DeviceTokensJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("device_tokens_json")
                .IsRequired(false);

            // Indexes and constraints
            builder.HasIndex(x => x.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("IX_users_normalized_email");

            builder.HasIndex(x => x.ProviderId)
                .HasDatabaseName("IX_users_provider_id");

            builder.HasIndex(x => x.SystemTypeId)
                .HasDatabaseName("IX_users_system_type_id");

            // Useful indexes for realtime queries
            builder.HasIndex(x => x.PresenceStatus)
                .HasDatabaseName("IX_users_presence_status");

            builder.HasIndex(x => x.LastSeenUtc)
                .HasDatabaseName("IX_users_last_seen_utc");

            // Seed data - deterministic UTC timestamp
            var seedTime = new DateTimeOffset(2025, 11, 19, 02, 00, 00, TimeSpan.Zero);

            builder.HasData(
                new UserEntity
                {
                    Id = 1000,
                    Email = "admin@example.edu",
                    NormalizedEmail = "admin@example.edu",
                    DisplayName = "System Admin",
                    FirstName = "System",
                    LastName = "Admin",
                    ProviderId = null,
                    AttributesJson = "{\"roles\":[\"Admin\",\"System\"],\"department\":\"IT\",\"createdBySeed\":true}",
                    ProfileJson = null,
                    SystemTypeId = 1,
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
                    NormalizedEmail = "faculty@example.edu",
                    DisplayName = "Faculty User",
                    FirstName = "Faculty",
                    LastName = "User",
                    ProviderId = null,
                    AttributesJson = "{\"roles\":[\"Faculty\"],\"department\":\"Engineering\",\"employmentType\":\"FullTime\"}",
                    ProfileJson = null,
                    SystemTypeId = 2,
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
                    NormalizedEmail = "student@example.edu",
                    DisplayName = "Student User",
                    FirstName = "Student",
                    LastName = "User",
                    ProviderId = null,
                    AttributesJson = "{\"roles\":[\"Student\"],\"gradeLevel\":\"Undergraduate\",\"enrolled\":true}",
                    ProfileJson = null,
                    SystemTypeId = 2,
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
                    NormalizedEmail = "external@example.edu",
                    DisplayName = "External User",
                    FirstName = "External",
                    LastName = "User",
                    ProviderId = null,
                    AttributesJson = "{\"roles\":[\"External\"],\"organization\":\"PartnerOrg\",\"contracted\":false}",
                    ProfileJson = null,
                    SystemTypeId = 3,
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
