using System;

namespace t5f25sdprojectone_projectsplus.Models
{
    public class Proposal
    {
        public int Id { get; set; }

        // Basic info
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string StudentName { get; set; } = string.Empty;

        // Faculty feedback
        public string? FacultyFeedback { get; set; }
        public DateTime? FeedbackDate { get; set; }

        // Optional approval
        public bool IsApproved { get; set; } = false;
    }
}
