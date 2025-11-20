// Models/Boards/KanbanCard.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    /// <summary>
    /// Card on a KanbanColumn.
    /// - ColumnId required; Project and Workspace scope inferred via Board->Column.
    /// - Position controls ordering within column.
    /// - Title required; Description optional (markdown/plain).
    /// - Supports Attachments (ProjectAttachment-like links) and Assignees (ProjectMember references).
    /// - ToString: "KanbanCard:{Id}:C:{ColumnId}:P:{Position}:T:{Title}:v{Version}"
    /// </summary>
    [Table("kanban_cards")]
    public class KanbanCard
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        [Required]
        [Column("column_id")]
        public long ColumnId { get; set; }

        [Required]
        [MaxLength(500)]
        [Column("title")]
        public string Title { get; set; } = null!;

        [MaxLength(8000)]
        [Column("description")]
        public string? Description { get; set; }

        [Required]
        [Column("position")]
        public int Position { get; set; } = 0;

        [Required]
        [Column("is_blocked")]
        public bool IsBlocked { get; set; } = false;

        [Required]
        [Column("is_archived")]
        public bool IsArchived { get; set; } = false;

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
        /// Navigation: comments/messages could reference card; attachments reference FileRecord via CardAttachment.
        /// </summary>
        public ICollection<KanbanCardAttachment> Attachments { get; set; } = new List<KanbanCardAttachment>();

        /// <summary>
        /// Navigation: assignees for the card (user ids / project member ids).
        /// </summary>
        public ICollection<KanbanCardAssignee> Assignees { get; set; } = new List<KanbanCardAssignee>();

        public override string ToString()
        {
            return $"KanbanCard:{Id}:C:{ColumnId}:P:{Position}:T:{Title}:v{Version}";
        }
    }

    // Lightweight attachment link specific to cards (stores FileRecordId)
    [Table("kanban_card_attachments")]
    public class KanbanCardAttachment
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }

        [Required]
        [Column("card_id")]
        public long CardId { get; set; }

        [Required]
        [Column("file_record_id")]
        public long FileRecordId { get; set; }

        [Required]
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("created_by_user_id")]
        public long CreatedByUserId { get; set; }

        [Required]
        [Column("version")]
        [ConcurrencyCheck]
        public int Version { get; set; } = 1;

        public override string ToString()
        {
            return $"KanbanCardAttachment:{Id}:Card:{CardId}:F:{FileRecordId}:v{Version}";
        }
    }

    // Lightweight assignee link
    [Table("kanban_card_assignees")]
    public class KanbanCardAssignee
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }

        [Required]
        [Column("card_id")]
        public long CardId { get; set; }

        [Required]
        [Column("project_member_id")]
        public long ProjectMemberId { get; set; } // FK to ProjectMember

        [Required]
        [Column("assigned_at")]
        public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("created_by_user_id")]
        public long CreatedByUserId { get; set; }

        [Required]
        [Column("version")]
        [ConcurrencyCheck]
        public int Version { get; set; } = 1;

        public override string ToString()
        {
            return $"KanbanCardAssignee:{Id}:Card:{CardId}:PM:{ProjectMemberId}:v{Version}";
        }
    }
}
