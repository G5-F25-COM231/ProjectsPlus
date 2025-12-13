using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
                    GithubRepoUrl = "https://github.com/XiaominGuo2025/CanadaCityWebAPIwithEFCore",
                    Contributions =
                    {
                        new Contribution{Id=1, Type=0, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User = new User { Id = 96, DisplayName = "Samantha Bee", Email = "samantha@example.com" }},
                        new Contribution{Id=2, Type=1, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User = new User { Id = 97, DisplayName = "Benjamin Carter", Email = "ben.c@example.com" }},
                        new Contribution{Id=3, Type=0, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User = new User { Id = 98, DisplayName = "Chloe Davies", Email = "chloe.d@example.com" }}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 1,
                            ProjectId = 1,
                            Title = "Setup database schema",
                            Status = 0, // To Do
                            Description = "Design and implement the initial SQL schema for user data and study sessions.",
                            DueDate = new DateTimeOffset(2025, 12, 10, 17, 0, 0, TimeSpan.Zero)
                        },
                        new ProjectTask {
                            Id = 2,
                            ProjectId = 1,
                            Title = "Design AI model architecture",
                            Status = 1, // In Progress
                            Description = "Finalize the choice between LSTM/Transformer and define layer structure for the core model.",
                            DueDate = new DateTimeOffset(2025, 12, 20, 17, 0, 0, TimeSpan.Zero)
                        },
                        new ProjectTask {
                            Id = 3,
                            ProjectId = 1,
                            Title = "Create wireframes",
                            Status = 3, // Done
                            Description = "Develop low-fidelity wireframes for the main dashboard and session interface.",
                            DueDate = new DateTimeOffset(2025, 11, 20, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },
                new Project
                {
                    Id = 2,
                    Title = "Campus Sustainability Dashboard",
                    Status = 3, // In Progress
                    SkillLevel = 1, // Beginner
                    GithubRepoUrl = "https://github.com/G5-F25-COM231/ProjectsPlus",
                    Contributions =
                    {
                        new Contribution{Id=4, Type=0, CreatedAt=DateTimeOffset.Now.AddDays(-3).AddHours(7), User = new User { Id = 99, DisplayName = "Daniel Evans", Email = "daniel.e@example.com" }},
                        new Contribution{Id=5, Type=0, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User = new User { Id = 100, DisplayName = "Emily Foster", Email = "emily.f@example.com" }}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 4,
                            ProjectId = 2,
                            Title = "Collect sustainability data",
                            Status = 1,
                            Description = "Gather energy consumption, waste, and water usage data from campus facilities.",
                            DueDate = new DateTimeOffset(2026, 01, 05, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },
                new Project
                {
                    Id = 3,
                    Title = "Open Source Contribution Tracker",
                    Status = 2, // New
                    SkillLevel = 3, // Advanced
                    GithubRepoUrl = "https://github.com/G5-F25-COM231/ProjectsPlus",
                    Contributions =
                    {
                        new Contribution{Id=6, Type=0, CreatedAt=DateTimeOffset.Now.AddSeconds(-30), User = new User { Id = 101, DisplayName = "Finn Green", Email = "finn.g@example.com" }},
                        new Contribution{Id=7, Type=0, CreatedAt=DateTimeOffset.Now.AddDays(-1).AddHours(-4), User = new User { Id = 102, DisplayName = "Grace Hall", Email = "grace.h@example.com" }}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 5,
                            ProjectId = 3,
                            Title = "Create contribution schema",
                            Status = 0,
                            Description = "Define the database structure to track pull requests, issues, and commit history for users.",
                            DueDate = new DateTimeOffset(2026, 01, 15, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },
                new Project
                {
                    Id = 4,
                    Title = "Student Forum Platform",
                    Status = 4, // Completed
                    SkillLevel = 2,
                    GithubRepoUrl = "https://github.com/G5-F25-COM231/ProjectsPlus",
                    Contributions =
                    {
                        new Contribution{Id=8, Type=0, CreatedAt=DateTimeOffset.Now.AddHours(-10), User = new User { Id = 103, DisplayName = "Henry Iyer", Email = "henry.i@example.com" }}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 6,
                            ProjectId = 4,
                            Title = "Setup forum database",
                            Status = 3,
                            Description = "Complete the initial database setup, including tables for posts, threads, and users.",
                            DueDate = new DateTimeOffset(2025, 11, 01, 17, 0, 0, TimeSpan.Zero)
                        },
                        new ProjectTask {
                            Id = 7,
                            ProjectId = 4,
                            Title = "Develop login system",
                            Status = 3,
                            Description = "Implement secure user authentication and registration features.",
                            DueDate = new DateTimeOffset(2025, 11, 10, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },
                new Project
                {
                    Id = 5,
                    Title = "AI Chatbot Assistant",
                    Status = 2, // new
                    SkillLevel = 3,
                    GithubRepoUrl = "https://github.com/G5-F25-COM231/ProjectsPlus",
                    Contributions =
                    {
                        new Contribution{Id=9, Type=0, CreatedAt=DateTimeOffset.Now.AddSeconds(-30), User = new User { Id = 104, DisplayName = "Samantha Bee", Email = "samantha@example.com" }}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 8,
                            ProjectId = 5,
                            Title = "Train NLP model 2rd",
                            Status = 3,
                            Description = "Run the second iteration of model training with the expanded dataset for improved response accuracy.",
                            DueDate = new DateTimeOffset(2025, 12, 15, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },

                new Project
                {
                    Id = 6,
                    Title = "IoT Smart Home System",
                    Status = 2, // New
                    SkillLevel = 4,
                    GithubRepoUrl = "https://github.com/XiaominGuo2025/CanadaCityWebAPIwithEFCore",
                    Contributions =
                    {
                        new Contribution{Id=10, Type=0, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User=new User{Id=105, DisplayName="Alex Johnson", Email="alex@gmail.com"} },
                        new Contribution{Id=11, Type=1, CreatedAt = DateTimeOffset.Now.AddDays(-3).AddHours(7), User=new User{Id=106, DisplayName="Maris Garcts", Email="maris@gmail.com"} },
                        new Contribution{Id=12, Type=1, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User=new User{Id=107, DisplayName="ChenWie", Email="wei@gmail.com"}},
                        new Contribution{Id=13, Type=1, CreatedAt = DateTimeOffset.Now.AddSeconds(-30), User=new User{Id=108, DisplayName="Sanmantha Bee", Email="bee@gmail.com"}}
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 9,
                            ProjectId = 6,
                            Title = "Sensor connection setup",
                            Status = 1,
                            Description = "Configure and establish communication protocols (MQTT/Bluetooth) for all smart sensors.",
                            DueDate = new DateTimeOffset(2026, 01, 25, 17, 0, 0, TimeSpan.Zero)
                        },
                        new ProjectTask {
                            Id = 10,
                            ProjectId = 6,
                            Title = "Device status dashboard",
                            Status = 1,
                            Description = "Develop a front-end interface to display real-time status and control connected IoT devices.",
                            DueDate = new DateTimeOffset(2026, 02, 10, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                },
                new Project
                {
                    Id = 7,
                    Title = "Campus Event Management System",
                    Status = 2, // New
                    SkillLevel = 2,
                    GithubRepoUrl = "https://github.com/G5-F25-COM231/ProjectsPlus",
                    Contributions =
                    {
                        new Contribution{Id=14, Type=0, CreatedAt = DateTimeOffset.Now.AddMinutes(-5), User=new User{Id=109, DisplayName = "Isabella Jones", Email = "isabella.j@example.com" } },
                        new Contribution{Id=15, Type=1, CreatedAt = DateTimeOffset.Now.AddDays(-1).AddHours(-4), User=new User{Id=110, DisplayName = "Jake King", Email = "jake.k@example.com" } },
                        new Contribution{Id=16, Type=1, CreatedAt = DateTimeOffset.Now.AddDays(-3).AddHours(7), User=new User{Id=111, DisplayName = "Liam Miller", Email = "liam.m@example.com" } },
                        new Contribution{Id=17, Type=0, CreatedAt = DateTimeOffset.Now.AddHours(-10), User=new User{Id=112, DisplayName = "Nora Nelson", Email = "nora.n@example.com" } }
                    },
                    Tasks = new List<ProjectTask>
                    {
                        new ProjectTask {
                            Id = 11,
                            ProjectId = 7,
                            Title = "Set up event schema",
                            Status = 0,
                            Description = "Create the database schema and initial tables for events, venues, and registration.",
                            DueDate = new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.Zero)
                        }
                    }
                }
            };



        // GET: /Student/NewProject
        public IActionResult NewProject()
        {
            return View();
        }

        // GET: /Student/ProjectDetails
        public IActionResult ProjectDetails(int projectId, string activeSidebar="Overview")
        {
            var project = projects.FirstOrDefault(p => p.Id == projectId);
            if (project == null)
                return NotFound();

            var model = new ProjectDetailsViewModel
            {
                Project = project,   // existing in-memory list
                CurrentUser = currentUser,
                CommitNumber = project.Contributions.Count(c => c.Type == 0),
                Members = project.Contributions.Where(c => c.Type == 0 && c.User != null).Select(c => c.User!).Distinct().ToList()

            };

            ViewData["ActiveSidebar"] = activeSidebar;

            return View(model);
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
                Projects = projects,   // existing in-memory list
                CurrentUser = currentUser,
                Tasks = projects.SelectMany(p => p.Tasks).ToList()  // add all tasks as a flat list
            };

            return View(model);
        }

    }
}
