namespace t5f25sdprojectone_projectsplus.Models
{
    public class MyProjectsViewModel
    {
        public List<Project> Projects { get; set; } = new List<Project>();
        public User CurrentUser { get; set; } = null!;
        public List<ProjectTask> Tasks { get; set; } = null!;
    }
}
