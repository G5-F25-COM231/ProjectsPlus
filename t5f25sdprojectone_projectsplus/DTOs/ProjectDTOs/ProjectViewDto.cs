using System;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.DTOs.ProjectDTOs
{
    public class ProjectViewDto
    {
        [JsonPropertyName("project")] public ProjectDto Project { get; set; }
        [JsonPropertyName("resourceRecords")] public object[] ResourceRecords { get; set; }   // placeholder; typed later
        [JsonPropertyName("lastStateChange")] public object LastStateChange { get; set; }   // ProjectStateChangeDto could be added
    }
}
