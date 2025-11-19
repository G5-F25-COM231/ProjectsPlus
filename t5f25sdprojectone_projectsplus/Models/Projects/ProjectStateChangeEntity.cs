using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.Projects
{
    public class ProjectStateChangeEntity : BaseEntity
    {
        public long ProjectId { get; set; }
        public string FromState { get; set; }
        public string ToState { get; set; }
        public long ActorUserId { get; set; }
        public Guid CorrelationId { get; set; }
        public string Message { get; set; }
        public string PayloadJson { get; set; }    // optional structured context
        public bool AllowRetry { get; set; } = false;
        public string RejectionType { get; set; }

        protected override string GetHumanKey()
        {
            return $"project:{ProjectId}:chg:{Id}";
        }

        public void SetPayloadFromObject(object obj)
        {
            if (obj == null) { PayloadJson = null; return; }
            PayloadJson = JsonSerializer.Serialize(obj);
        }
    }
}
