using System;

namespace t5f25sdprojectone_projectsplus.Models
{
    public class InfralogEntity
    {
        public long Id { get; set; }
        public string CorrelationId { get; set; }           // required
        public string Category { get; set; }                // required, e.g., "Provision", "Audit"
        public string Message { get; set; }                 // required
        public string DetailsJson { get; set; }             // optional structured details
        public long? ActorUserId { get; set; }              // optional
        public int Version { get; set; }
        public bool IsDeleted { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public override string ToString()
        {
            return $"Infralog[{Id}] {CorrelationId} {Category} v{Version}";
        }
    }
}
