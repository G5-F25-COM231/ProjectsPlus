// Models/Integrations/GitHubProjectLink.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    [Table("github_project_links")]
    public class GitHubProjectLink
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        [Required]
        [Column("owner_user_id")]
        public long OwnerUserId { get; set; }

        [Required]
        [MaxLength(2000)]
        [Column("remote_url")]
        public string RemoteUrl { get; set; } = null!;

        [MaxLength(2000)]
        [Column("external_id")]
        public string? ExternalId { get; set; }

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
            return $"GitHubProjectLink:{Id}:U:{OwnerUserId}:R:{RemoteUrl}:v{Version}";
        }
    }
}
