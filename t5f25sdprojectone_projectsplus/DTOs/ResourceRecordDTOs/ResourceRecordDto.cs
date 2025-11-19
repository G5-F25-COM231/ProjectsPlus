using System;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.ResourceRecordDTOs
{
    public class ResourceRecordDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("provider")] public string Provider { get; set; }
        [JsonPropertyName("providerResourceId")] public string ProviderResourceId { get; set; }
        [JsonPropertyName("profileJson")] public object ProfileJson { get; set; }
        [JsonPropertyName("correlationId")] public Guid CorrelationId { get; set; }
        [JsonPropertyName("projectId")] public long? ProjectId { get; set; }
        [JsonPropertyName("workspaceId")] public long? WorkspaceId { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
