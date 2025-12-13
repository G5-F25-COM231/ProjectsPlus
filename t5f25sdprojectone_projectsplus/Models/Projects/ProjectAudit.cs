// src/Models/Projects/ProjectAudit.cs
using System;

namespace t5f25sdprojectone_projectsplus.Models.Projects
{
    public sealed class ProjectAudit
    {
        // Surrogate primary key for EF and queries
        public long Id { get; set; }

        // Optimistic concurrency token; incremented by application when appropriate
        public int Version { get; set; }

        // Kept from your original shape
        public string Entity { get; set; } = null!;
        public long EntityId { get; set; }
        public string Action { get; set; } = null!;

        // Standard audit timestamps
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        // Optional JSON payload; do not store secrets
        public string? Data { get; set; }

        public override string ToString() => $"{Entity}:{EntityId}:{Action}:{CreatedAt:o}";
    }
}
