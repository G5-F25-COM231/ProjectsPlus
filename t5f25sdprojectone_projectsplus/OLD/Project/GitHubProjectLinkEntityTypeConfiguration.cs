// Configurations/Integrations/GitHubProjectLinkEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    public class GitHubProjectLinkEntityTypeConfiguration : IEntityTypeConfiguration<GitHubProjectLink>
    {
        public void Configure(EntityTypeBuilder<GitHubProjectLink> builder)
        {
            builder.ToTable("github_project_links");

            builder.HasKey(g => g.Id);
            builder.Property(g => g.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(GitHubProjectLink.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_github_projectlink_system_type_id_system_types");


            builder.Property(g => g.OwnerUserId).HasColumnName("owner_user_id").IsRequired();
            builder.Property(g => g.RemoteUrl).HasColumnName("remote_url").HasMaxLength(2000).IsRequired();
            builder.Property(g => g.ExternalId).HasColumnName("external_id").HasMaxLength(2000).IsRequired(false);
            builder.Property(g => g.CreatedAt).HasColumnName("created_at").IsRequired();
            builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").IsRequired();
            builder.Property(g => g.Version).HasColumnName("version").IsRequired().IsConcurrencyToken();

            builder.HasIndex(g => g.OwnerUserId).HasDatabaseName("ix_github_project_links_owner_user_id");
            builder.HasIndex(g => g.ExternalId).HasDatabaseName("ix_github_project_links_external_id");
        }
    }
}
