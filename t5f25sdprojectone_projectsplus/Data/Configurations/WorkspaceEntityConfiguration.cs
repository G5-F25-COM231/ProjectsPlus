// src/ProjectsPlus.Data/Configurations/WorkspaceEntityConfiguration.cs
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Data.Configurations
{
    public class WorkspaceEntityConfiguration : IEntityTypeConfiguration<WorkspaceEntity>
    {
        public void Configure(EntityTypeBuilder<WorkspaceEntity> builder)
        {
            builder.ToTable("Workspaces");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ProjectId)
                .IsRequired()
                .HasColumnName("project_id");

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(256)
                .HasColumnName("name");

            builder.Property(x => x.Slug)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnName("slug");

            builder.Property(x => x.State)
                .HasConversion<int>()
                .HasDefaultValue(WorkspaceState.ProvisioningPending)
                .HasColumnName("state");

            builder.Property(x => x.MetadataJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("metadata_json");

            builder.Property(x => x.OwnerUserId)
                .IsRequired()
                .HasColumnName("owner_user_id");

            builder.Property(x => x.Version)
                .IsConcurrencyToken()
                .HasDefaultValue(1)
                .HasColumnName("version");

            builder.Property(x => x.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");

            builder.Property(x => x.CreatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired()
                .HasColumnName("created_at");

            builder.Property(x => x.UpdatedAt)
                .HasColumnType("datetimeoffset")
                .IsRequired()
                .HasColumnName("updated_at");

            // New communications-related JSON columns
            builder.Property(x => x.SettingsJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("settings_json")
                .IsRequired(false);

            builder.Property(x => x.DefaultNotificationPreferencesJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("default_notification_preferences_json")
                .IsRequired(false);

            builder.Property(x => x.RetentionPolicyJson)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("retention_policy_json")
                .IsRequired(false);

            // Unique constraint: slug scoped to project (unique per project)
            builder.HasIndex(x => new { x.ProjectId, x.Slug }).IsUnique().HasDatabaseName("UX_workspaces_project_slug");

            builder.HasIndex(x => x.ProjectId).HasDatabaseName("IX_workspaces_project_id");
            builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("IX_workspaces_owner_user_id");
        }
    }
}
