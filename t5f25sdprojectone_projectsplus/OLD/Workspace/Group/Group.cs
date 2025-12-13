// Models/Workspace/Group.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.Group
{
    /// <summary>
    /// Team/Group within a workspace.
    /// - WorkspaceId ties the group to its workspace.
    /// - Slug unique within workspace for stable referencing.
    /// - Description optional; navigation to members exists via GroupMember.
    /// - Deterministic ToString: "Group:{Id}:W:{WorkspaceId}:S:{Slug}:v{Version}"
    /// </summary>
    [Table("groups")]
    public class Group
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
        /// Optional navigation to members; initialized to avoid NREs.
        /// </summary>
        public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();

        public override string ToString()
        {
            return $"Group:{Id}:W:{WorkspaceId}:S:{Slug}:v{Version}";
        }
    }
}
