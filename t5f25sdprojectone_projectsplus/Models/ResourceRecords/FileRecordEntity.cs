using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.ResourceRecords
{
    public class FileRecordEntity : BaseEntity
    {
        public long OwnerUserId { get; set; }
        public long? ProjectId { get; set; }
        public int SystemTypeId { get; set; }
        public string StorageKey { get; set; }           // e.g., s3://bucket/key
        public string ChecksumSha256 { get; set; }       // lowercase hex
        public long SizeBytes { get; set; }
        public string MimeType { get; set; }
        public string OriginalFileName { get; set; }
        public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;
        public string QuarantineReason { get; set; }
        public bool IsDeleted { get; internal set; }

        protected override string GetHumanKey()
        {
            return OriginalFileName ?? $"file-{Id}";
        }
    }
}
