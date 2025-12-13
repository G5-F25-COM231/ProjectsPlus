using System;
using System.Text.Json.Serialization;
using t5f25sdprojectone_projectsplus.Models.Projects;

namespace t5f25sdprojectone_projectsplus.DTOs.ProjectDTOs
{
    public class ProjectDto
    {
        [JsonPropertyName("projectId")] public long ProjectId { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("shortDescription")] public string ShortDescription { get; set; }
        [JsonPropertyName("longDescription")] public string LongDescription { get; set; }
        [JsonPropertyName("ownerUserId")] public long OwnerUserId { get; set; }
        [JsonPropertyName("workspaceId")] public long? WorkspaceId { get; set; }
        [JsonPropertyName("status")] public ProjectStatus Status { get; set; }
        [JsonPropertyName("additionOfId")] public long? AdditionOfId { get; set; }
        [JsonPropertyName("additionType")] public string AdditionType { get; set; }
        [JsonPropertyName("requiresBaseConsent")] public bool RequiresBaseConsent { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
