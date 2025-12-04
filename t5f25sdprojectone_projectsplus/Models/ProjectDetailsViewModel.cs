namespace t5f25sdprojectone_projectsplus.Models
{
    public class ProjectDetailsViewModel
    {
        public Project Project { get; set; } = null!;
        public User CurrentUser { get; set; } = null!;
        public List<User> Members { get; set; } = new List<User>();
        public int CommitNumber {  get; set; } 
    }
}
