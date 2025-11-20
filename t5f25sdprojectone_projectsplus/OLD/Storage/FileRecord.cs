// Models/Storage/FileRecord.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Storage
{
    /// <summary>
    /// Immutable descriptor for a single uploaded file version and its processing state.
    /// ToString: "FileRecord:{Id}:F:{Filename}:C:{Checksum}:S:{Status}:v{Version}"
    /// </summary>
    [Table("file_records")]
    public class FileRecord
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        // inside FileRecord POCO
        [Required]
        [Column("system_type_id")]
        public long SystemTypeId { get; set; }

        [Required]
        [MaxLength(2000)]
        [Column("storage_path")]
        public string StoragePath { get; set; } = null!; // deterministic object key

        [Required]
        [MaxLength(1000)]
        [Column("filename")]
        public string Filename { get; set; } = null!;

        [Required]
        [MaxLength(200)]
        [Column("content_type")]
        public string ContentType { get; set; } = null!;

        [Required]
        [Column("size_bytes")]
        public long SizeBytes { get; set; }

        [Required]
        [MaxLength(128)]
        [Column("checksum")]
        public string Checksum { get; set; } = null!; // sha256 hex

        [Required]
        [Column("status")]
        public int Status { get; set; } = 0; // enum: UploadPending, Scanning, Available, Rejected, Quarantined, Archived

        [MaxLength(8000)]
        [Column("profile_json")]
        public string? ProfileJson { get; set; } // scan results, derived metadata

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
        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("version")]
        [ConcurrencyCheck]
        public int Version { get; set; } = 1;

        public override string ToString()
        {
            return $"FileRecord:{Id}:F:{Filename}:C:{Checksum}:S:{Status}:v{Version}";
        }
    }
}
