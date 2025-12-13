// Models/Project/ProjectMember.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    /// <summary>
    /// Membership for a user on a project.
    /// - ProjectId and UserId form a unique pair.
    /// - Role stored as int enum (Owner, Admin, Contributor, Viewer, etc).
    /// - JoinedAt/RemovedAt represent lifecycle; RemovedAt != null indicates soft-removed.
    /// - Repositories must set CreatedAt/UpdatedAt and increment Version on updates.
    /// - ToString: "ProjectMember:{Id}:P:{ProjectId}:U:{UserId}:R:{Role}:v{Version}"
    /// </summary>
    [Table("project_members")]
    public class ProjectMember
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        [Required]
        [Column("project_id")]
        public long ProjectId { get; set; }

        [Required]
        [Column("user_id")]
        public long UserId { get; set; }

        [Required]
        [Column("role")]
        public int Role { get; set; }

        [Column("joined_at")]
        public DateTimeOffset? JoinedAt { get; set; }

        [Column("removed_at")]
        public DateTimeOffset? RemovedAt { get; set; }

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
            return $"ProjectMember:{Id}:P:{ProjectId}:U:{UserId}:R:{Role}:v{Version}";
        }
    }
}
