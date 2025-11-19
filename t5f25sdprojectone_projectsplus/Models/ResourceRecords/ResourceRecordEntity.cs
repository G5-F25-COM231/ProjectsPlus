using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.ResourceRecords
{
    /// <summary>
    /// Canonical provenance record mapping internal domain to external provider resource.
    /// Unique constraint MUST be enforced on (provider, providerResourceId) at DB layer.
    /// profileJson must NOT contain raw secrets; only secretRef identifiers.
    /// </summary>
    public class ResourceRecordEntity : BaseEntity
    {
        public string Provider { get; set; }                    // e.g., "github", "aws-s3"
        public string ProviderResourceId { get; set; }          // provider unique id/ARN
        public string ProfileJson { get; set; }                 // minified metadata (repoUrl, eTag, secretRef)
        public Guid CorrelationId { get; set; }                 // originating operation correlation id
        public long? ProjectId { get; set; }                    // optional linkage
        public long? WorkspaceId { get; set; }
        public long? RelatedToProjectId { get; set; }           // reuse linking
        public int SystemTypeId { get; set; }                   // references SystemType seed
        public bool IsDeleted { get; set; } = false;

        protected override string GetHumanKey()
        {
            // use providerResourceId as human key (truncate if needed)
            return ProviderResourceId ?? $"{Provider}:{Id}";
        }

        public void SetProfileFromObject(object obj)
        {
            if (obj == null) { ProfileJson = null; return; }
            ProfileJson = JsonSerializer.Serialize(obj);
        }
    }
}
