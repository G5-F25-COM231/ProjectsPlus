// Models/Boards/KanbanBoard.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    /// <summary>
    /// Kanban board. Can be workspace-scoped (ProjectId = null) or project-scoped (ProjectId != null).
    /// ToString: "KanbanBoard:{Id}:W:{WorkspaceId}:P:{ProjectId}:S:{Slug}:v{Version}"
    /// </summary>
    [Table("kanban_boards")]
    public class KanbanBoard
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

        [Column("project_id")]
        public long? ProjectId { get; set; }

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
        /// Navigation for columns; initialized to avoid NREs.
        /// </summary>
        public ICollection<KanbanColumn> Columns { get; set; } = new List<KanbanColumn>();

        public override string ToString()
        {
            return $"KanbanBoard:{Id}:W:{WorkspaceId}:P:{ProjectId?.ToString() ?? "null"}:S:{Slug}:v{Version}";
        }
    }
}
