using System;
using System.Text.Json.Serialization;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.DTOs.WorkspaceResponseDTOs
{
    public class WorkspaceResponseDto
    {
        [JsonPropertyName("workspaceId")] public long WorkspaceId { get; set; }
        [JsonPropertyName("projectId")] public long ProjectId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("slug")] public string Slug { get; set; }
        [JsonPropertyName("state")] public WorkspaceState State { get; set; }
        [JsonPropertyName("metadataJson")] public object MetadataJson { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
