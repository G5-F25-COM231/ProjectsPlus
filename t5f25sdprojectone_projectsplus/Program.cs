using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.RegExtension;

namespace t5f25sdprojectone_projectsplus
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Common services
            builder.Services.AddRazorPages();

            // ---------------------------------------------------------
            // Option A: Register DbContext from configuration (recommended)
            // Commented out by request — uncomment to enable.
            // Make sure you have a connection string named "ProjectsPlus"
            // in appsettings.json or your environment.
            // ---------------------------------------------------------
            /*
            builder.Services.AddDbContext<ProjectsPlusDbContext>(opts =>
                opts.UseSqlite(builder.Configuration.GetConnectionString("ProjectsPlus")));

            // Register ProjectsPlus services after DbContext registration
            builder.Services.AddProjectsPlus();
            */

            // ---------------------------------------------------------
            // Option B: Convenience single-call registration
            // Active by default (keeps Program minimal). For demos/local
            // runs you may prefer InMemory; for relational tests replace
            // UseInMemoryDatabase with UseSqlite or UseSqlServer.
            // ---------------------------------------------------------
            builder.Services.AddProjectsPlusWithDb(opts =>
                opts.UseInMemoryDatabase("projectsplus_dev"));

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapRazorPages();

            app.Run();
        }
    }
}
