// src/Data/EntityConfigurations/ProjectStateChangeEntityConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Project
{
    /// <summary>
    /// EF Core mapping for ProjectStateChangeEntity
    /// - Keeps a compact audit trail of state transitions for a project
    /// - Designed for append-mostly workloads; indexed by ProjectId for efficient lookup
    /// - Stores correlation id and payload JSON for observability and replay
    /// </summary>
    public class ProjectStateChangeEntityConfiguration : IEntityTypeConfiguration<ProjectStateChangeEntity>
    {
        public void Configure(EntityTypeBuilder<ProjectStateChangeEntity> builder)
        {
            // Table and primary key
            builder.ToTable("project_state_changes");
            builder.HasKey(x => x.Id);

            // Core relationship / identity
            builder.Property(x => x.ProjectId).IsRequired();
            builder.HasIndex(x => x.ProjectId).HasDatabaseName("ix_project_state_changes_project_id");

            // State names and actor
            builder.Property(x => x.FromState).HasMaxLength(100);
            builder.Property(x => x.ToState).HasMaxLength(100);
            builder.Property(x => x.ActorUserId).IsRequired();

            // Correlation and payload for observability
            builder.Property(x => x.CorrelationId).IsRequired().HasMaxLength(100);
            builder.Property(x => x.PayloadJson).HasColumnType("text");

            // Audit columns
            builder.Property(x => x.CreatedAt).IsRequired();
            builder.Property(x => x.UpdatedAt).IsRequired(false);

            // Version available for idempotency or optimistic checks on the change itself if needed
            // Default: not used as concurrency token for the change rows (append-only)
            builder.Property(x => x.Version).IsConcurrencyToken(false);

            // Optional: soft-delete support if Present on the entity
            if (typeof(ProjectStateChangeEntity).GetProperty("IsDeleted") != null)
            {
                builder.Property<bool>("IsDeleted").HasColumnName("is_deleted").HasDefaultValue(false);
                builder.HasQueryFilter(e => EF.Property<bool>(e, "IsDeleted") == false);
            }

            // Ensure a concise clustered/indexed scan pattern; ordering by CreatedAt helps most queries
            // Note: some providers allow filtered indexes or include columns; add provider-specific enhancements later.
            builder.HasIndex(b => new { b.ProjectId, b.CreatedAt }).HasDatabaseName("ix_project_state_changes_project_created_at");
        }
    }
}
