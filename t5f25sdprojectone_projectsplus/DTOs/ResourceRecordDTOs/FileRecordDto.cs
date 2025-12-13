using System;
using System.Text.Json.Serialization;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;

namespace t5f25sdprojectone_projectsplus.DTOs.ResourceRecordDTOs
{
    public class FileRecordDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("ownerUserId")] public long OwnerUserId { get; set; }
        [JsonPropertyName("projectId")] public long? ProjectId { get; set; }
        [JsonPropertyName("storageKey")] public string StorageKey { get; set; }
        [JsonPropertyName("checksumSha256")] public string ChecksumSha256 { get; set; }
        [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
        [JsonPropertyName("mimeType")] public string MimeType { get; set; }
        [JsonPropertyName("originalFileName")] public string OriginalFileName { get; set; }
        [JsonPropertyName("scanStatus")] public ScanStatus ScanStatus { get; set; }
        [JsonPropertyName("quarantineReason")] public string QuarantineReason { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
