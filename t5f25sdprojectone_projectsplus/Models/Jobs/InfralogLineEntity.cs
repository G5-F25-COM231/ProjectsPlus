using System;

namespace t5f25sdprojectone_projectsplus.Models.Jobs
{
    /// <summary>
    /// Represents a single canonical infralog line persisted in DB (indexable) in addition to the external blob storage.
    /// The canonical line fields are stored; sha256Hex is the checksum computed over the first nine fields joined by '|'.
    /// </summary>
    public class InfralogLineEntity : BaseEntity
    {
        public DateTimeOffset TimestampUtc { get; set; }
        public string EnsureType { get; set; }             // e.g., EnsureGitHubRepo
        public string ResourceKey { get; set; }            // e.g., project:123:repo:main
        public string Provider { get; set; }               // e.g., github
        public string ProviderResourceId { get; set; }     // provider id/arn
        public Guid CorrelationId { get; set; }
        public long ActorUserId { get; set; }              // 0 for system/service
        public string Region { get; set; }                 // provider region or null
        public string MetadataJson { get; set; }           // minified metadata (secretRef only)
        public string Sha256Hex { get; set; }              // lowercase hex of sha256 over canonical nine fields

        protected override string GetHumanKey()
        {
            return $"{EnsureType}:{ResourceKey}";
        }
    }
}
