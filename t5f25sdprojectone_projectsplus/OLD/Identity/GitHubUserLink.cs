// Models/Identity/GitHubUserLink.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// Canonical GitHub identity mapping.
    /// - Maps a GitHub account (numeric provider id) optionally to a local user.
    /// - Keeps small profile snapshot (login, avatar_url), ETag for conditional fetch, and last_synced_at.
    /// - CreatedAt/UpdatedAt maintained by repository logic.
    /// - Version used for optimistic concurrency.
    /// - Deterministic ToString:
    ///   "GitHubUserLink:{Id}:G:{GitHubUserId}:L:{Login}:v{Version}"
    /// </summary>
    [Table("github_user_links")]
    public class GitHubUserLink
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        /// <summary>
        /// Local user FK; nullable because not every GitHub identity is linked to a local account.
        /// </summary>
        [Column("local_user_id")]
        public long? LocalUserId { get; set; }

        [Required]
        [Column("github_user_id")]
        public long GitHubUserId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("login")]
        public string Login { get; set; } = null!;

        [MaxLength(1000)]
        [Column("avatar_url")]
        public string? AvatarUrl { get; set; }

        [MaxLength(200)]
        [Column("etag")]
        public string? ETag { get; set; }

        [Column("last_synced_at")]
        public DateTimeOffset? LastSyncedAt { get; set; }

        [MaxLength(4000)]
        [Column("profile_json")]
        public string? ProfileJson { get; set; }

        [Required]
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("version")]
        [ConcurrencyCheck]
        public int Version { get; set; } = 1;

        public override string ToString()
        {
            return $"GitHubUserLink:{Id}:G:{GitHubUserId}:L:{Login}:v{Version}";
        }
    }
}
