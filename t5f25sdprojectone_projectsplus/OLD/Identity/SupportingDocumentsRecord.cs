// Models/Identity/SupportingDocumentsRecord.cs
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace t5f25sdprojectone_projectsplus.OLD.Identity
{
    /// <summary>
    /// Metadata record for an uploaded supporting documents archive (ZIP).
    /// - Store only metadata and a secure object-store path/key; do not persist raw file bytes.
    /// - Numeric long Id PK.
    /// - Immutable once written except for Version increment on administrative corrections.
    /// - Keep ToString safe for logs (no paths or checksums printed in full).
    /// </summary>
    [Table("supporting_documents")]
    public sealed class SupportingDocumentsRecord
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Required]
        [Column("user_id")]
        public long UserId { get; set; }

        // Object store key or secure path; not the file contents
        [Required]
        [MaxLength(2000)]
        [Column("archive_path")]
        public string ArchivePath { get; set; } = null!;

        [Required]
        [MaxLength(512)]
        [Column("file_name")]
        public string FileName { get; set; } = null!;

        [Required]
        [Column("content_length")]
        public long ContentLength { get; set; }

        // Optional small checksum (e.g., SHA256 hex) to verify integrity; keep trimmed length
        [MaxLength(128)]
        [Column("checksum")]
        public string? Checksum { get; set; }

        [MaxLength(45)]
        [Column("uploaded_by_ip")]
        public string? UploadedByIp { get; set; }

        [Required]
        [Column("uploaded_at")]
        public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [ConcurrencyCheck]
        [Column("version")]
        public int Version { get; set; } = 1;

        // Small note field for admin corrections or annotations; not required
        [MaxLength(2000)]
        [Column("notes")]
        public string? Notes { get; set; }

        // Convenience: active flag (archive may be revoked)
        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        // For safe logging. Omits ArchivePath and Checksum contents to avoid accidental leakage.
        public override string ToString()
        {
            string? shortPath = ArchivePath != null && ArchivePath.Length > 40 ? ArchivePath.Substring(0, 36) + "..." : ArchivePath;
            string? shortChecksum = !string.IsNullOrEmpty(Checksum) && Checksum.Length > 12 ? Checksum.Substring(0, 12) + "..." : Checksum;
            return $"SupportingDocuments:{Id}:User={UserId}:File={FileName}:Len={ContentLength}:Chk={shortChecksum}:UploadedAt={UploadedAt:O}:Active={IsActive}:Ver={Version}";
        }
    }
}
