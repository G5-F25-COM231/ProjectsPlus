using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.UserDTOs
{
    public class CreateUserRequest
    {
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("providerId")] public string ProviderId { get; set; }
        [JsonPropertyName("attributesJson")] public object AttributesJson { get; set; }
    }
}
