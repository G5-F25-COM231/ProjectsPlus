using Microsoft.AspNetCore.Mvc;
using t5f25sdprojectone_projectsplus.Models;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    public class StudentController : Controller
    {
        // Sample data
        private static User currentUser = new User
        {
            DisplayName = "Olivia Rhye",
            Email = "olivia@example.com",
            Verified = true,
            CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        private static List<Project> projects = new List<Project>
            {
                new Project
                {
                    Id = 1,
                    Title = "AI-Powered Study Assistant",
                    Status = 3, // In Progress
                    SkillLevel = 2, // Intermediate
                    GithubRepoUrl = "https://github.com/sample/ai-study-assistant",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 1, Title = "Setup database schema", Status = 0 },   // To Do
                        new ProjectTask { Id = 2, Title = "Design AI model architecture", Status = 1 }, // In Progress
                        new ProjectTask { Id = 3, Title = "Create wireframes", Status = 3 }        // Done
                    }
                },
                new Project
                {
                    Id = 2,
                    Title = "Campus Sustainability Dashboard",
                    Status = 3, // In Progress
                    SkillLevel = 1, // Beginner
                    GithubRepoUrl = "https://github.com/sample/campus-dashboard",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 4, Title = "Collect sustainability data", Status = 1 },
                        new ProjectTask { Id = 5, Title = "Design dashboard UI", Status = 0 },
                    }
                },
                new Project
                {
                    Id = 3,
                    Title = "Open Source Contribution Tracker",
                    Status = 2, // New
                    SkillLevel = 3, // Advanced
                    GithubRepoUrl = "https://github.com/sample/campus-dashboard",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 6, Title = "Create contribution schema", Status = 0 }
                    }
                },
                new Project
                {
                    Id = 4,
                    Title = "Student Forum Platform",
                    Status = 4, // Completed
                    SkillLevel = 2,
                    GithubRepoUrl = "https://github.com/sample/campus-dashboard",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 7, Title = "Setup forum database", Status = 3 },
                        new ProjectTask { Id = 8, Title = "Develop login system", Status = 3 }
                    }
                },
                new Project
                {
                    Id = 5,
                    Title = "AI Chatbot Assistant",
                    Status = 2, // new
                    SkillLevel = 3,
                    GithubRepoUrl = "https://github.com/sample/aichatbot",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 9, Title = "Train NLP model 2rd", Status = 3 },
                        new ProjectTask { Id = 10, Title = "Build chat API endpoints", Status = 0 }
                    }
                },

                new Project
                {
                    Id = 6,
                    Title = "IoT Smart Home System",
                    Status = 2, // New
                    SkillLevel = 4,
                    GithubRepoUrl = "https://github.com/sample/recommendation-engine",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 11, Title = "Sensor connection setup", Status = 1 },
                        new ProjectTask { Id = 12, Title = "Device status dashboard", Status = 1 }
                    }
                },

                new Project
                {
                    Id = 7,
                    Title = "E-Commerce Recommendation",
                    Status = 3,
                    SkillLevel = 3,
                    GithubRepoUrl = "https://github.com/sample/recommendation-engine",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 13, Title = "Collect product data", Status = 1 },
                        new ProjectTask { Id = 14, Title = "Build collaborative filtering model", Status = 1 }
                    }
                },

                new Project
                {
                    Id = 8,
                    Title = "Job Application Tracker",
                    Status = 3,
                    SkillLevel = 1,
                    GithubRepoUrl = "https://github.com/sample/job-tracker",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 15, Title = "Create job entry form", Status = 3 },
                    }
                },

                new Project
                {
                    Id = 9,
                    Title = "Fitness Tracker Mobile App",
                    Status = 4, // Ongoing
                    SkillLevel = 2,
                    GithubRepoUrl = "https://github.com/sample/job-tracker",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 16, Title = "Design UI mockups", Status = 0 }
                    }
                },

                new Project
                {
                    Id = 10,
                    Title = "Campus Event Management System",
                    Status = 2, // New
                    SkillLevel = 2,
                    GithubRepoUrl = "https://github.com/sample/job-tracker",
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask { Id = 17, Title = "Set up event schema", Status = 0 }
                    }
                }
            };

        // GET: /Student/NewProject
        public IActionResult NewProject()
        {
            return View();
        }

        // POST: /Student/NewProject
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult NewProject(Project model)
        {
            // Assign new Id
            model.Id = projects.Max(p => p.Id) + 1;

            // Generate Slug from Title
            model.Slug = model.Title?.ToLower().Replace(" ", "-") ?? $"project-{model.Id}";
            // Initialize empty Tasks list
            model.Tasks = new List<ProjectTask>();

            model.CreatedAt = DateTimeOffset.UtcNow;
            model.UpdatedAt = DateTimeOffset.UtcNow;
            model.Status = 2; // Default "New"

            // Add to in-memory list
            projects.Add(model);

            //if (ModelState.IsValid)
            //{
                // After adding the project
                return RedirectToAction("MyProjects", new { activeTab = "New" });

            //}

            //return View(model);
        }

        // GET: /Student/MyProjects
        public IActionResult MyProjects(string activeSidebar = "MyProjects", string activeTab = "Ongoing")
        {
            ViewData["ActiveSidebar"] = activeSidebar;  // "MyProjects" or "Profile"
            ViewData["ActiveTab"] = activeTab;          // "Ongoing", "Completed", "New"

            //return View(projects); // pass your projects list as before
            var model = new MyProjectsViewModel
            {
                Projects = projects,   // your existing in-memory list
                CurrentUser = currentUser
            };

            return View(model);
        }

    }
}
