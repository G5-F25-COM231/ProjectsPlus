// Configurations/Identity/GitHubUserLinkEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// EF Core configuration for GitHubUserLink.
    /// - Table "github_user_links"
    /// - Unique index on github_user_id
    /// - Index on login
    /// - FK local_user_id -> users.id with Restrict delete behavior
    /// - Version is concurrency token
    /// </summary>
    public class GitHubUserLinkEntityTypeConfiguration : IEntityTypeConfiguration<GitHubUserLink>
    {
        public void Configure(EntityTypeBuilder<GitHubUserLink> builder)
        {
            builder.ToTable("github_user_links");

            builder.HasKey(g => g.Id);
            builder.Property(g => g.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(GitHubUserLink.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_github_userlinks_system_type_id_system_types");

            builder.Property(g => g.LocalUserId)
                   .HasColumnName("local_user_id")
                   .IsRequired(false);

            builder.Property(g => g.GitHubUserId)
                   .HasColumnName("github_user_id")
                   .IsRequired();

            builder.Property(g => g.Login)
                   .HasColumnName("login")
                   .HasMaxLength(200)
                   .IsRequired();

            builder.Property(g => g.AvatarUrl)
                   .HasColumnName("avatar_url")
                   .HasMaxLength(1000)
                   .IsRequired(false);

            builder.Property(g => g.ETag)
                   .HasColumnName("etag")
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.Property(g => g.LastSyncedAt)
                   .HasColumnName("last_synced_at")
                   .IsRequired(false);

            builder.Property(g => g.ProfileJson)
                   .HasColumnName("profile_json")
                   .HasMaxLength(4000)
                   .IsRequired(false);

            builder.Property(g => g.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(g => g.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(g => g.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(g => g.GitHubUserId)
                   .IsUnique()
                   .HasDatabaseName("ix_github_user_links_github_user_id");

            builder.HasIndex(g => g.Login)
                   .HasDatabaseName("ix_github_user_links_login");

            // FK to users table; do not cascade delete local user removal.
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(GitHubUserLink.LocalUserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_github_user_links_local_user_id_users");
                       
        }
    }
}
