using System;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.UserDTOs
{
    public class UserDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("providerId")] public string ProviderId { get; set; }
        [JsonPropertyName("attributesJson")] public object AttributesJson { get; set; }
        [JsonPropertyName("isActive")] public bool IsActive { get; set; }
        [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
