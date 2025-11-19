using System;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.JodDTOs
{
    public class LaunchJobLogDto
    {
        [JsonPropertyName("jobId")] public string JobId { get; set; }
        [JsonPropertyName("projectId")] public long ProjectId { get; set; }
        [JsonPropertyName("correlationId")] public Guid CorrelationId { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; }
        [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; set; }
        [JsonPropertyName("finishedAt")] public DateTimeOffset? FinishedAt { get; set; }
        [JsonPropertyName("attempts")] public int Attempts { get; set; }
        [JsonPropertyName("outcomeJson")] public object OutcomeJson { get; set; }
    }
}
