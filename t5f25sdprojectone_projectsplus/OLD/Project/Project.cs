// Models/Project/Project.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    /// <summary>
    /// Project owned by a workspace.
    /// - WorkspaceId links project to its workspace.
    /// - OwnerUserId is the project-level owner (may differ from workspace owner).
    /// - Slug is unique within a workspace.
    /// - Status and Visibility stored as compact ints (enums in service layer).
    /// - GithubLinkId optional FK to GitHubProjectLink for provider integrations.
    /// - Deterministic ToString: "Project:{Id}:W:{WorkspaceId}:S:{Slug}:v{Version}"
    /// </summary>
    [Table("projects")]
    public class Project
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        [Required]
        [Column("workspace_id")]
        public long WorkspaceId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("name")]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        [Column("slug")]
        public string Slug { get; set; } = null!;

        [Required]
        [Column("owner_user_id")]
        public long OwnerUserId { get; set; }

        /// <summary>
        /// Status enum stored as int (e.g., Draft=0, Launched=1, Archived=2)
        /// </summary>
        [Required]
        [Column("status")]
        public int Status { get; set; } = 0;

        /// <summary>
        /// Visibility enum stored as int (e.g., Private=0, Public=1)
        /// </summary>
        [Required]
        [Column("visibility")]
        public int Visibility { get; set; } = 0;

        [Column("github_link_id")]
        public long? GithubLinkId { get; set; }

        [MaxLength(8000)]
        [Column("description")]
        public string? Description { get; set; }

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

        /// <summary>
        /// Optional navigation back to workspace; useful for eager loading when needed.
        /// </summary>
        public Workspace.Workspace? Workspace { get; set; }

        /// <summary>
        /// Navigation collection for members; initialized to avoid NREs.
        /// </summary>
        public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
        
        /// <summary>
        /// Navigation: attachments linked to this project (file metadata only).
        /// Initialized to avoid null refs in tests and mapping code.
        /// </summary>
        public ICollection<ProjectAttachment> Attachments { get; set; } = new List<ProjectAttachment>();

        public override string ToString()
        {
            return $"Project:{Id}:W:{WorkspaceId}:S:{Slug}:v{Version}";
        }
    }
}
