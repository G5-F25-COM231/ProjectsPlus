using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.JodDTOs
{
    public class JobResponseDto
    {
        [JsonPropertyName("jobId")] public string JobId { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("correlationId")] public string CorrelationId { get; set; }
    }
}
