using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.ProjectDTOs
{
    public class SubmitProjectRequest
    {
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("attachments")] public long[] Attachments { get; set; }
    }
}
