// Example EF entity (move or adapt to your existing Data project)

namespace t5f25sdprojectone_projectsplus.Models.Audit
{
    public class AuditEntryEntity
    {
        public long Id { get; set; }
        public DateTime TimestampUtc { get; set; }
        public long? ActorUserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }
}
