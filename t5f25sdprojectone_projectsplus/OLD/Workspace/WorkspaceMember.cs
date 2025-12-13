// Models/Workspace/WorkspaceMember.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace
{
    /// <summary>
    /// Membership record for a user in a workspace.
    /// - WorkspaceId and UserId form a unique pair.
    /// - Role stored as int enum (e.g., Owner, Admin, Member, Guest).
    /// - InviteToken/InviteExpiresAt support invite flows.
    /// - JoinedAt/RemovedAt represent membership lifecycle; RemovedAt != null indicates soft-removed.
    /// - Repositories must set CreatedAt/UpdatedAt and increment Version on updates.
    /// - Deterministic ToString: "WorkspaceMember:{Id}:W:{WorkspaceId}:U:{UserId}:R:{Role}:v{Version}"
    /// </summary>
    [Table("workspace_members")]
    public class WorkspaceMember
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
        [Column("user_id")]
        public long UserId { get; set; }

        [Required]
        [Column("role")]
        public int Role { get; set; }

        [Column("joined_at")]
        public DateTimeOffset? JoinedAt { get; set; }

        [Column("removed_at")]
        public DateTimeOffset? RemovedAt { get; set; }

        [MaxLength(200)]
        [Column("invite_token")]
        public string? InviteToken { get; set; }

        [Column("invite_expires_at")]
        public DateTimeOffset? InviteExpiresAt { get; set; }

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
            return $"WorkspaceMember:{Id}:W:{WorkspaceId}:U:{UserId}:R:{Role}:v{Version}";
        }
    }
}
