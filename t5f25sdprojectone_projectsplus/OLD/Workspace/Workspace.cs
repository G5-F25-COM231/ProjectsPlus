// Models/Workspace/Workspace.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Octokit;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace
{
    /// <summary>
    /// Top-level workspace container.
    /// - OwnerUserId is the administrative owner (FK to users.id).
    /// - Projects is a navigation collection (one-to-many).
    /// - DefaultVisibility is an int enum (0=Private,1=Public).
    /// - Repositories must set CreatedAt/UpdatedAt and increment Version on updates.
    /// - Deterministic ToString: "Workspace:{Id}:S:{Slug}:Owner:{OwnerUserId}:v{Version}"
    /// </summary>
    [Table("workspaces")]
    public class Workspace
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }

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
        /// 0 = Private, 1 = Public
        /// </summary>
        [Required]
        [Column("default_visibility")]
        public int DefaultVisibility { get; set; } = 0;

        [MaxLength(4000)]
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
        /// Navigation: projects owned by this workspace.
        /// Keep initialized to avoid null refs in tests and mapping code.
        /// </summary>
        public ICollection<Project.Project> Projects { get; set; } = new List<Project.Project>();

        public override string ToString()
        {
            return $"Workspace:{Id}:S:{Slug}:Owner:{OwnerUserId}:v{Version}";
        }
    }
}
