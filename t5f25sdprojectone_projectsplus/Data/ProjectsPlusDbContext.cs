using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data.EntityConfigurations;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Configurations;
using t5f25sdprojectone_projectsplus.Models.Jobs;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Data
{
    public class ProjectsPlusDbContext : DbContext
    {
        public ProjectsPlusDbContext(DbContextOptions<ProjectsPlusDbContext> options) : base(options) { }
       
        // Core domain DbSets
        public DbSet<UserEntity> Users { get; set; }
        public DbSet<ProjectEntity> Projects { get; set; }
        public DbSet<ResourceRecordEntity> ResourceRecords { get; set; }
        public DbSet<FileRecordEntity> FileRecords { get; set; }
        public DbSet<WorkspaceEntity> Workspaces { get; set; }

        // Operational logs and job artifacts
        public DbSet<InfralogEntity> Infralogs { get; set; }
        public DbSet<JobLogEntity> JobLogs { get; set; }

        public DbSet<ProjectAudit> ProjectAudits { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply all entity configurations
            modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ProjectEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ResourceRecordEntityConfiguration());
            modelBuilder.ApplyConfiguration(new FileRecordEntityConfiguration());
            modelBuilder.ApplyConfiguration(new WorkspaceEntityConfiguration());
            modelBuilder.ApplyConfiguration(new InfralogEntityConfiguration());
            modelBuilder.ApplyConfiguration(new JobLogEntityConfiguration());
            modelBuilder.ApplyConfiguration(new Models.SystemTypeID.SystemTypeEntityTypeConfiguration());


            // If there are additional configurations (seeds, cross-table indexes, FK conventions),
            // they can be added here in a deterministic order.
        }
    }
}
