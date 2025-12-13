// Configurations/System/SystemTypeEntityTypeConfiguration.cs
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace t5f25sdprojectone_projectsplus.Models.SystemTypeID
{
    /// <summary>
    /// EF Core entity configuration for system_types
    /// - Aligns column names with migrations (snake_case)
    /// - Declares unique index on code for deterministic lookups
    /// - Contains a single, clearly-marked seed block where new SystemType rows are added
    /// - Keep HasData Ids stable: add one new deterministic seed line per new type
    /// 
    /// ID allocation policy (read before adding seeds):
    ///  1) Keep existing stable IDs unchanged. These IDs may be referenced by code, migrations, or backfill scripts.
    ///  2) Reserve ID ranges by domain to reduce collisions:
    ///       - 1..99   : core project/file/board/card canonical types
    ///       - 100..199: workspace/user/system admin related types (preferred range for canonical user/system seeds)
    ///       - 200..299: integrations / external links
    ///       - 1000+   : experimental or ephemeral types (avoid unless intentional)
    ///  3) When adding a new type:
    ///       - Pick the next unused ID in the appropriate range (do not reuse deleted IDs).
    ///       - Add a single-line HasData entry (matching the existing single-line style).
    ///       - If a migration already inserted this row, align Id/Code with that migration to avoid double-insert conflicts.
    ///  4) If you detect an ID collision on review, choose the next unused ID in the range and update the seed only (do not renumber stable IDs).
    /// </summary>
    public sealed class SystemTypeEntityTypeConfiguration : IEntityTypeConfiguration<SystemType>
    {
        public void Configure(EntityTypeBuilder<SystemType> builder)
        {
            builder.ToTable("system_types");

            // Primary key and identity generation
            builder.HasKey(t => t.Id).HasName("pk_system_types");
            builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedOnAdd();

            // Columns mapped to snake_case to match migrations
            builder.Property(t => t.Code).HasColumnName("code").HasMaxLength(200).IsRequired();
            builder.Property(t => t.DisplayName).HasColumnName("display_name").HasMaxLength(400).IsRequired();
            builder.Property(t => t.MetadataJson).HasColumnName("metadata_json").IsRequired(false);
            builder.Property(t => t.IsActive).HasColumnName("is_active").IsRequired();
            builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(t => t.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            // Indexes
            builder.HasIndex(t => t.Code)
                .IsUnique()
                .HasDatabaseName("ux_system_types_code");

            // --------------------------------------------------------------
            // Deterministic seeds for domain stability
            // - Add ONE new SystemType object here each time a new canonical type is required.
            // - Use a stable numeric Id (do not reuse or renumber).
            // - Keep CreatedAt/UpdatedAt deterministic if downstream backfill/migration logic depends on them.
            // - If Migration A already inserted seeds via MigrationBuilder.InsertData, align these entries with those migrations.
            // - Single-line entries are preferred for readability in HasData.
            // --------------------------------------------------------------
            builder.HasData(
                // Projects
                new SystemType { Id = 1, Code = "project:standard", DisplayName = "Project — standard", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 2, Code = "project:template", DisplayName = "Project — template", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Files
                new SystemType { Id = 10, Code = "file:generic", DisplayName = "File — generic", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 11, Code = "file:pdf", DisplayName = "File — PDF", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 12, Code = "file:zip", DisplayName = "File — ZIP", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 13, Code = "file:evidence", DisplayName = "File — legal evidence", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 14, Code = "file:image", DisplayName = "File — image", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 15, Code = "file:thumbnail", DisplayName = "File — thumbnail", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Attachments / Card attachments
                new SystemType { Id = 20, Code = "attachment:generic", DisplayName = "Attachment — generic", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 60, Code = "card_attachment", DisplayName = "Card Attachment", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Boards and columns
                new SystemType { Id = 30, Code = "board:user", DisplayName = "Board — user", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 31, Code = "board:system", DisplayName = "Board — system", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 40, Code = "column:standard", DisplayName = "Kanban Column — standard", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Cards and card types
                new SystemType { Id = 50, Code = "card:standard", DisplayName = "Kanban Card — standard", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 51, Code = "card:blocker", DisplayName = "Kanban Card — blocker", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 61, Code = "card_assignee", DisplayName = "Card Assignee", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Project membership and roles
                new SystemType { Id = 70, Code = "project:member", DisplayName = "Project Member", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Integrations / links
                new SystemType { Id = 80, Code = "github:project-link", DisplayName = "GitHub Project Link", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 81, Code = "github:user-link", DisplayName = "GitHub User Link", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Release/build artifacts and exports
                new SystemType { Id = 16, Code = "file:release", DisplayName = "File — release artifact", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Workspace and reserved user code
                new SystemType { Id = 90, Code = "workspace:standard", DisplayName = "Workspace — standard", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // System/admin artifacts (stable existing IDs)
                new SystemType { Id = 110, Code = "system:notification", DisplayName = "System Notification", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },
                new SystemType { Id = 111, Code = "system:audit", DisplayName = "System Audit Log", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-11-13T00:00:00Z"), Version = 1 },

                // Core user/system seeds (placed in 100..199 range; adjusted to avoid collisions with existing 110/111)
                // Use these IDs as the canonical user/system ids for domain code that expects stable numeric references.
                new SystemType { Id = 112, Code = "user:internal", DisplayName = "Internal User", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), Version = 1 },
                new SystemType { Id = 113, Code = "user:external", DisplayName = "External User", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), Version = 1 },
                new SystemType { Id = 114, Code = "system:service", DisplayName = "Service Account", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), Version = 1 },

                //user entity
                new SystemType { Id = 115, Code = "user:userentity", DisplayName = "User — userentity", MetadataJson = null, IsActive = true, CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"), Version = 1 }


            );
        }
    }
}
