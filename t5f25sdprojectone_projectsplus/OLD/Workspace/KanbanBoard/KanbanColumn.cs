// Models/Boards/KanbanColumn.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.KanbanBoard
{
    /// <summary>
    /// Column on a KanbanBoard.
    /// - BoardId required
    /// - Position (int) controls ordering
    /// - Slug stable within board
    /// - ToString: "KanbanColumn:{Id}:B:{BoardId}:S:{Slug}:Pos:{Position}:v{Version}"
    /// </summary>
    [Table("kanban_columns")]
    public class KanbanColumn
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }


        [Required]
        [Column("board_id")]
        public long BoardId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("name")]
        public string Name { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        [Column("slug")]
        public string Slug { get; set; } = null!;

        [Required]
        [Column("position")]
        public int Position { get; set; } = 0;

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
        /// Navigation: cards in this column; initialized to avoid NREs.
        /// </summary>
        public ICollection<KanbanCard> Cards { get; set; } = new List<KanbanCard>();

        public override string ToString()
        {
            return $"KanbanColumn:{Id}:B:{BoardId}:S:{Slug}:Pos:{Position}:v{Version}";
        }
    }
}
