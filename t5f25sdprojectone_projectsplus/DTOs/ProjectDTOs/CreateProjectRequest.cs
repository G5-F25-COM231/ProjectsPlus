using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.ProjectDTOs
{
    public class CreateProjectRequest
    {
        [JsonPropertyName("title")] public required string Title { get; set; }
        [JsonPropertyName("shortDescription")] public required string ShortDescription { get; set; }
        [JsonPropertyName("longDescription")] public required string LongDescription { get; set; }
        [JsonPropertyName("ownerUserId")] public long OwnerUserId { get; set; }
        [JsonPropertyName("additionOfId")] public long? AdditionOfId { get; set; }
        [JsonPropertyName("additionType")] public string AdditionType { get; set; }
        [JsonPropertyName("additionCompatibilityJson")] public object AdditionCompatibilityJson { get; set; }
        [JsonPropertyName("requiresBaseConsent")] public bool? RequiresBaseConsent { get; set; }
    }
}
