using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.UserDTOs
{
    public class UpdateUserRequest
    {
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("attributesJson")] public object AttributesJson { get; set; }
        [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
