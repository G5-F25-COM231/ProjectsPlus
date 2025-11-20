using System;

namespace t5f25sdprojectone_projectsplus.Models.Projects
{
    public sealed class ProjectAudit
    {
        public string Entity { get; set; } = null!;
        public long EntityId { get; set; }
        public string Action { get; set; } = null!;
        public DateTimeOffset Timestamp { get; set; }
        public string? Data { get; set; }

        public override string ToString() => $"{Entity}:{EntityId}:{Action}:{Timestamp:o}";
    }
}
