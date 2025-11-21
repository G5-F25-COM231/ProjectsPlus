// src/Data/ProjectsPlusDbContext.cs
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data.EntityConfigurations;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Audit;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Models.Configurations;
using t5f25sdprojectone_projectsplus.Models.Configurations.Audit;
using t5f25sdprojectone_projectsplus.Models.Configurations.Authorization;
using t5f25sdprojectone_projectsplus.Models.Configurations.Opsconfigs;
using t5f25sdprojectone_projectsplus.Models.Configurations.Project;
using t5f25sdprojectone_projectsplus.Models.Jobs;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Data
{
    /// <summary>
    /// EF Core DbContext for ProjectsPlus.
    /// - Centralizes DbSet declarations for domain, operational, audit, and authorization models.
    /// - Applies IEntityTypeConfiguration implementations in a deterministic, reviewable order.
    /// - Keep this class minimal: configuration lives in separate configuration types so OnModelCreating remains clear.
    /// </summary>
    public class ProjectsPlusDbContext : DbContext
    {
        public ProjectsPlusDbContext(DbContextOptions<ProjectsPlusDbContext> options) : base(options) { }

        // Core domain DbSets
        public DbSet<UserEntity> Users { get; set; } = null!;
        public DbSet<ProjectEntity> Projects { get; set; } = null!;
        public DbSet<ResourceRecordEntity> ResourceRecords { get; set; } = null!;
        public DbSet<FileRecordEntity> FileRecords { get; set; } = null!;
        public DbSet<WorkspaceEntity> Workspaces { get; set; } = null!;

        // Operational logs and job artifacts
        public DbSet<InfralogEntity> Infralogs { get; set; } = null!;
        public DbSet<JobLogEntity> JobLogs { get; set; } = null!;

        // Auditing and change history
        public DbSet<ProjectAudit> ProjectAudits { get; set; } = null!;
        public DbSet<ProjectStateChangeEntity> ProjectStateChanges { get; set; } = null!;

        // Authorization model (Phase 5)
        // These DbSets back the policy-driven authorization subsystem.
        // They are read-heavy and should be tuned (indexes, caching) in migrations/ops.
        public DbSet<Permission> Permissions { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<RolePermission> RolePermissions { get; set; } = null!;
        public DbSet<UserRole> UserRoles { get; set; } = null!;

        // Add the audit entries DbSet
        public DbSet<RefreshTokenEntity> RefreshTokens { get; set; } = null!;

        // AuditEntries DbSet assumed already added per prior steps
        public DbSet<AuditEntryEntity> AuditEntries { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply domain configurations in deterministic order.
            // Keep the call order explicit to avoid surprising FK/index ordering across providers.
            modelBuilder.ApplyConfiguration(new UserEntityConfiguration());
            modelBuilder.ApplyConfiguration(new WorkspaceEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ProjectEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ProjectStateChangeEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ResourceRecordEntityConfiguration());
            modelBuilder.ApplyConfiguration(new FileRecordEntityConfiguration());
            modelBuilder.ApplyConfiguration(new InfralogEntityConfiguration());
            modelBuilder.ApplyConfiguration(new JobLogEntityConfiguration());
            modelBuilder.ApplyConfiguration(new ProjectAuditConfiguration());
            modelBuilder.ApplyConfiguration(new Models.SystemTypeID.SystemTypeEntityTypeConfiguration());

            // Authorization configurations (Phase 5)
            // These map Roles, Permissions, RolePermission and UserRole to dedicated tables.
            modelBuilder.ApplyConfiguration(new PermissionConfiguration());
            modelBuilder.ApplyConfiguration(new RoleConfiguration());
            modelBuilder.ApplyConfiguration(new RolePermissionConfiguration());
            modelBuilder.ApplyConfiguration(new UserRoleConfiguration());

            // Audit
            modelBuilder.ApplyConfiguration(new AuditEntryEntityConfiguration());
            modelBuilder.ApplyConfiguration(new RefreshTokenEntityConfiguration());
       
            // Notes for operators and reviewers:
            // - Keep cross-table indexes and FK constraints defined in configuration classes.
            // - For large deployments, consider partitioning RolePermission or indexing RoleId first for efficient permission checks.
            // - If operations prefer fewer tables, we can provide a compact variant (Role.PermissionsJson) but it trades queryability and referential integrity for operational simplicity.
            // - Any schema changes should include migration scripts, backfill plans for existing users, and a rollback strategy.
        }

        internal object? AddProjectsPlusWithInMemoryDb()
        {
            throw new NotImplementedException();
        }
    }
}
