using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models;
using System.Collections.Generic;
using System.Linq;

namespace t5f25sdprojectone_projectsplus.Pages.Tasks
{
    public class MyTasksModel : PageModel
    {
        private readonly ProjectsPlusDbContext _db;

        public List<ProjectTask> AssignedTasks { get; set; } = new();
        public bool IsLoading { get; set; } = true;

        public MyTasksModel(ProjectsPlusDbContext db)
        {
            _db = db;
        }

        public void OnGet()
        {
            AssignedTasks = _db.ProjectTasks
                .OrderBy(t => t.DueDate)
                .ToList();

            IsLoading = false;
        }

        public IActionResult OnPostMarkDone(int id)
        {
            var task = _db.ProjectTasks.FirstOrDefault(t => t.Id == id);
            if (task == null)
                return NotFound();

            task.IsDone = true;
            _db.SaveChanges();

            return RedirectToPage();
        }
    }
}
