// Models/Identity/User.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// Canonical User POCO for Identity domain.
    /// - Numeric long Id PK.
    /// - SystemTypeId kept as numeric; nullable during staged rollout.
    /// - NormalizedUsername/NormalizedEmail for server-side uniqueness and lookups.
    /// - PasswordHash and PasswordSalt are nullable to allow external/federated accounts.
    /// - SignupRoute and SignupStatus capture signup variant and lifecycle.
    /// - JoiningPurpose and SupportingDocumentsPath capture External signup metadata (metadata only).
    /// - DefaultPasswordIssuedAt marks when a one-time/default password was emailed.
    /// - Version used for optimistic concurrency; UpdatedAt maintained by repository logic.
    /// - ToString omits secrets and payloads for safe logging.
    /// </summary>
    [Table("users")]
    public sealed class User
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("system_type_id")]
        public long? SystemTypeId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("username")]
        public string Username { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        [Column("normalized_username")]
        public string NormalizedUsername { get; set; } = null!;

        [Required]
        [MaxLength(320)]
        [Column("email")]
        public string Email { get; set; } = null!;

        [Required]
        [MaxLength(320)]
        [Column("normalized_email")]
        public string NormalizedEmail { get; set; } = null!;

        [MaxLength(1000)]
        [Column("password_hash")]
        public string? PasswordHash { get; set; }

        [MaxLength(500)]
        [Column("password_salt")]
        public string? PasswordSalt { get; set; }

        [Column("email_verified_at")]
        public DateTimeOffset? EmailVerifiedAt { get; set; }

        [Column("last_login_at")]
        public DateTimeOffset? LastLoginAt { get; set; }

        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        // Additional fields surfaced per your request / snippet
        [MaxLength(200)]
        [Column("display_name")]
        public string? DisplayName { get; set; }

        [Required]
        [Column("verified")]
        public bool Verified { get; set; } = false;

        [Column("github_id")]
        public long? GithubId { get; set; }

        [Required]
        [Column("signup_route")]
        public SignupRoute SignupRoute { get; set; } = SignupRoute.SelfService;

        [Required]
        [Column("signup_status")]
        public SignupStatus SignupStatus { get; set; } = SignupStatus.Active;

        [MaxLength(4000)]
        [Column("joining_purpose")]
        public string? JoiningPurpose { get; set; }

        [MaxLength(2000)]
        [Column("supporting_documents_path")]
        public string? SupportingDocumentsPath { get; set; }

        [Column("is_admin_seeded")]
        public bool IsAdminSeeded { get; set; } = false;

        [Column("default_password_issued_at")]
        public DateTimeOffset? DefaultPasswordIssuedAt { get; set; }

        [MaxLength(4000)]
        [Column("profile_json")]
        public string? ProfileJson { get; set; }

        [Column("created_by")]
        public long? CreatedBy { get; set; }

        [Required]
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [ConcurrencyCheck]
        [Column("version")]
        public int Version { get; set; } = 1;

        // Convenience helpers (not mapped)
        [NotMapped]
        public bool NeedsApproval => SignupRoute == SignupRoute.External && SignupStatus == SignupStatus.PendingApproval;

        [NotMapped]
        public bool MustChangePassword => DefaultPasswordIssuedAt != null && string.IsNullOrWhiteSpace(PasswordHash);

        public override string ToString()
        {
            return $"User:{Id}:SysType={SystemTypeId?.ToString() ?? "n/a"}:U={NormalizedUsername}:E={NormalizedEmail}:Route={SignupRoute}:Status={SignupStatus}:Active={IsActive}:Ver={Version}";
        }

        public void SetProfile(object profile)
        {
            ProfileJson = JsonSerializer.Serialize(profile);
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
