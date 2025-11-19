using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.Workspaces
{
    public class WorkspaceEntity : BaseEntity
    {
        public long ProjectId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }                   // unique per workspace (scoped)
        public WorkspaceState State { get; set; } = WorkspaceState.ProvisioningPending;
        public string MetadataJson { get; set; }           // provider references and metadata
        public long OwnerUserId { get; set; }
        public bool IsDeleted { get; internal set; }

        protected override string GetHumanKey()
        {
            return Slug ?? $"ws-{Id}";
        }

        public void SetMetadataFromObject(object obj)
        {
            if (obj == null) { MetadataJson = null; return; }
            MetadataJson = JsonSerializer.Serialize(obj);
        }
    }
}
