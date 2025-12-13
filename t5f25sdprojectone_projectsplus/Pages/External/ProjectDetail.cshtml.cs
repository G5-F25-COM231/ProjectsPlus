using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace t5f25sdprojectone_projectsplus.Pages.External
{
    [AllowAnonymous] // external visitors can view this
    public class ProjectDetailModel : PageModel
    {
        public ProjectViewModel Project { get; private set; } = default!;

        // Treat this as our “public project catalog”
        private static readonly List<ProjectViewModel> PublicProjects = new()
        {
            new ProjectViewModel(
                Id: "ai-powered-tutoring",
                Title: "AI-Powered Tutoring Platform",
                Subtitle: "Adaptive learning support for students.",
                Description: "Adaptive tutoring system that personalizes learning content based on student performance and engagement.",
                Tags: new[] { "AI", "Education", "MachineLearning" },
                ParticipantCount: 3
            ),
            new ProjectViewModel(
                Id: "sustainable-energy",
                Title: "Sustainable Energy Solutions",
                Subtitle: "Student-led renewable energy initiative.",
                Description: "Explores renewable energy prototypes and campus sustainability interventions.",
                Tags: new[] { "RenewableEnergy", "Sustainability" },
                ParticipantCount: 2
            ),
            new ProjectViewModel(
                Id: "mental-wellness-app",
                Title: "Mobile App for Mental Wellness",
                Subtitle: "Supporting mental health via mobile.",
                Description: "Cross-platform mobile experience offering mood tracking, exercises, and connections to campus resources.",
                Tags: new[] { "MentalHealth", "MobileApp" },
                ParticipantCount: 1
            )
        };

        public IActionResult OnGet(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            // Look up only in the PUBLIC list.
            var project = PublicProjects.FirstOrDefault(p => p.Id == id);

            if (project == null)
            {
                // ✅ This is your “private project blocked” behavior:
                // anything not in the public list is treated as forbidden.
                return Forbid();
            }

            Project = project;
            return Page();
        }

        public record ProjectViewModel(
            string Id,
            string Title,
            string Subtitle,
            string Description,
            string[] Tags,
            int ParticipantCount
        );
    }
}
