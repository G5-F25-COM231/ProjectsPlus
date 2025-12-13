// Models/Project/ProjectAttachment.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Project
{
    /// <summary>
    /// Link between a Project and a FileRecord version.
    /// ToString: "ProjectAttachment:{Id}:P:{ProjectId}:F:{FileRecordId}:R:{Role}:v{Version}"
    /// </summary>
    [Table("project_attachments")]
    public class ProjectAttachment
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
        [Column("file_record_id")]
        public long FileRecordId { get; set; }

        [Required]
        [MaxLength(200)]
        [Column("role")]
        public string Role { get; set; } = "supporting-doc"; // semantic role

        [Column("uploaded_for_project_status")]
        public int? UploadedForProjectStatus { get; set; } // enum snapshot (Draft, Launched, ...)

        [Required]
        [Column("created_by_user_id")]
        public long CreatedByUserId { get; set; }

        [MaxLength(200)]
        [Column("correlation_id")]
        public string? CorrelationId { get; set; }

        [Required]
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("version")]
        [ConcurrencyCheck]
        public int Version { get; set; } = 1;

        public override string ToString()
        {
            return $"ProjectAttachment:{Id}:P:{ProjectId}:F:{FileRecordId}:R:{Role}:v{Version}";
        }
    }
}
