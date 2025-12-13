// Configurations/Identity/UserEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// EF Core mapping for the users table.
    /// - Table name: users
    /// - Columns use snake_case explicitly
    /// - Unique indexes: ux_users_normalized_email, ux_users_normalized_username
    /// - Non-unique indexes: ix_users_signup_status, ix_users_system_type_id, ix_users_github_id
    /// - Concurrency token: version (int) enforced via [ConcurrencyCheck] on the POCO
    /// - FK to system_types is intentionally deferred to Migration C (see migrations/backfill plan).
    /// </summary>
    public sealed class UserEntityTypeConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> b)
        {
            b.ToTable("users");

            b.HasKey(x => x.Id)
             .HasName("pk_users");

            b.Property(x => x.Id)
             .HasColumnName("id");

            b.Property(x => x.SystemTypeId)
             .HasColumnName("system_type_id");

            b.Property(x => x.Username)
             .HasColumnName("username")
             .HasMaxLength(200)
             .IsRequired();

            b.Property(x => x.NormalizedUsername)
             .HasColumnName("normalized_username")
             .HasMaxLength(200)
             .IsRequired();

            b.Property(x => x.Email)
             .HasColumnName("email")
             .HasMaxLength(320)
             .IsRequired();

            b.Property(x => x.NormalizedEmail)
             .HasColumnName("normalized_email")
             .HasMaxLength(320)
             .IsRequired();

            b.Property(x => x.PasswordHash)
             .HasColumnName("password_hash")
             .HasMaxLength(1000);

            b.Property(x => x.PasswordSalt)
             .HasColumnName("password_salt")
             .HasMaxLength(500);

            b.Property(x => x.EmailVerifiedAt)
             .HasColumnName("email_verified_at");

            b.Property(x => x.LastLoginAt)
             .HasColumnName("last_login_at");

            b.Property(x => x.IsActive)
             .HasColumnName("is_active")
             .IsRequired();

            b.Property(x => x.DisplayName)
             .HasColumnName("display_name")
             .HasMaxLength(200)
             .IsRequired(false);

            b.Property(x => x.Verified)
             .HasColumnName("verified")
             .IsRequired()
             .HasDefaultValue(false);

            b.Property(x => x.GithubId)
             .HasColumnName("github_id")
             .IsRequired(false);

            b.Property(x => x.SignupRoute)
             .HasColumnName("signup_route")
             .IsRequired();

            b.Property(x => x.SignupStatus)
             .HasColumnName("signup_status")
             .IsRequired();

            b.Property(x => x.JoiningPurpose)
             .HasColumnName("joining_purpose")
             .HasMaxLength(4000);

            b.Property(x => x.SupportingDocumentsPath)
             .HasColumnName("supporting_documents_path")
             .HasMaxLength(2000);

            b.Property(x => x.IsAdminSeeded)
             .HasColumnName("is_admin_seeded")
             .IsRequired();

            b.Property(x => x.DefaultPasswordIssuedAt)
             .HasColumnName("default_password_issued_at");

            b.Property(x => x.ProfileJson)
             .HasColumnName("profile_json")
             .HasMaxLength(4000);

            b.Property(x => x.CreatedBy)
             .HasColumnName("created_by");

            b.Property(x => x.CreatedAt)
             .HasColumnName("created_at")
             .IsRequired();

            b.Property(x => x.UpdatedAt)
             .HasColumnName("updated_at")
             .IsRequired();

            b.Property(x => x.Version)
             .HasColumnName("version")
             .IsRequired();

            // Indexes
            b.HasIndex(x => x.NormalizedEmail)
             .IsUnique()
             .HasDatabaseName("ux_users_normalized_email");

            b.HasIndex(x => x.NormalizedUsername)
             .IsUnique()
             .HasDatabaseName("ux_users_normalized_username");

            b.HasIndex(x => x.SignupStatus)
             .HasDatabaseName("ix_users_signup_status");

            b.HasIndex(x => x.SystemTypeId)
             .HasDatabaseName("ix_users_system_type_id");

            b.HasIndex(x => x.GithubId)
             .HasDatabaseName("ix_users_github_id");

            // NOTE: FK to system_types is deferred to Migration C after backfill.
            // To enable FK in mapping after Migration C, uncomment and use the explicit constraint name:
            //
            // b.HasOne<SystemType>()
            //  .WithMany()
            //  .HasForeignKey(nameof(User.SystemTypeId))
            //  .HasConstraintName("fk_users_system_type_id_system_types");
        }
    }
}
