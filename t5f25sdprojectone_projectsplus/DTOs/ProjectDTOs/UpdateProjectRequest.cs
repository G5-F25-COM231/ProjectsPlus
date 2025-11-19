using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.ProjectDTOs
{
    public class UpdateProjectRequest
    {
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("shortDescription")] public string ShortDescription { get; set; }
        [JsonPropertyName("longDescription")] public string LongDescription { get; set; }
        [JsonPropertyName("additionCompatibilityJson")] public object AdditionCompatibilityJson { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
    }
}
