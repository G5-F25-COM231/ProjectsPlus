// src/Data/EntityConfigurations/ProjectAuditConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.Data.EntityConfigurations
{
    public class ProjectAuditConfiguration : IEntityTypeConfiguration<ProjectAudit>
    {
        public void Configure(EntityTypeBuilder<ProjectAudit> builder)
        {
            builder.ToTable("project_audits");

            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(x => x.Version)
                .HasColumnName("version")
                .IsConcurrencyToken()
                .HasDefaultValue(0);

            builder.Property(x => x.Entity)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnName("entity");

            builder.Property(x => x.EntityId)
                .IsRequired()
                .HasColumnName("entity_id");

            builder.Property(x => x.Action)
                .IsRequired()
                .HasMaxLength(200)
                .HasColumnName("action");

            builder.Property(x => x.CreatedAt)
                .IsRequired()
                .HasColumnName("created_at");

            builder.Property(x => x.UpdatedAt)
                .IsRequired(false)
                .HasColumnName("updated_at");

            builder.Property(x => x.Data)
                .HasColumnType("text")
                .HasColumnName("data");

            builder.HasIndex(x => x.EntityId).HasDatabaseName("ix_project_audits_entity_id");
            builder.HasIndex(x => x.Entity).HasDatabaseName("ix_project_audits_entity");
            builder.HasIndex(b => new { b.Entity, b.EntityId, b.CreatedAt }).HasDatabaseName("ix_project_audits_entity_entityid_created_at");
        }
    }
}
