using System;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.JodDTOs
{
    public class InfralogLineDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("timestampUtc")] public DateTimeOffset TimestampUtc { get; set; }
        [JsonPropertyName("ensureType")] public string EnsureType { get; set; }
        [JsonPropertyName("resourceKey")] public string ResourceKey { get; set; }
        [JsonPropertyName("provider")] public string Provider { get; set; }
        [JsonPropertyName("providerResourceId")] public string ProviderResourceId { get; set; }
        [JsonPropertyName("correlationId")] public Guid CorrelationId { get; set; }
        [JsonPropertyName("actorUserId")] public long ActorUserId { get; set; }
        [JsonPropertyName("region")] public string Region { get; set; }
        [JsonPropertyName("metadataJson")] public object MetadataJson { get; set; }
        [JsonPropertyName("sha256Hex")] public string Sha256Hex { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
