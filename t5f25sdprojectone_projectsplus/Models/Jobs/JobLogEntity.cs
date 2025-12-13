using System;

namespace t5f25sdprojectone_projectsplus.Models.Jobs
{
    /// <summary>
    /// Generic job log entity. Use scopeKey to enforce uniqueness per-scope (e.g., project:{id}).
    /// For Launch jobs: scopeKey = project:{projectId}; correlationId enforces idempotency across callers.
    /// </summary>
    public class JobLogEntity : BaseEntity
    {
        public string JobId { get; set; }                  // UUID or similar
        public string JobType { get; set; }                // e.g., LaunchJob, ProvisionJob
        public string ScopeKey { get; set; }               // e.g., project:123
        public Guid CorrelationId { get; set; }            // client-provided correlation id
        public string Status { get; set; }                 // Queued | InProgress | Completed | Failed | Partial
        public int Attempts { get; set; } = 0;
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public string OutcomeJson { get; set; }            // structured outcome or error info
        public string OutcomeCode { get; set; }            // optional short code
        public string Owner { get; set; }                  // system/service owner label
        public string UniqueKey => $"{ScopeKey}|{CorrelationId}"; // helpful helper (not persisted)

        public bool IsDeleted { get; internal set; }

        protected override string GetHumanKey()
        {
            return JobId ?? $"job-{Id}";
        }
    }
}
