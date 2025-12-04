// src/Data/ProjectsPlusDbContext.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using t5f25sdprojectone_projectsplus.Data.Configurations;
using t5f25sdprojectone_projectsplus.Data.Configurations.Audit;
using t5f25sdprojectone_projectsplus.Data.Configurations.Authorization;
using t5f25sdprojectone_projectsplus.Data.Configurations.Communication;
using t5f25sdprojectone_projectsplus.Data.Configurations.Opsconfigs;
using t5f25sdprojectone_projectsplus.Data.Configurations.Project;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Audit;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Models.Jobs;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.ResourceRecords;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches;
using t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.MssqlAdapter;

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
        public DbSet<Models.Users.UserEntity> Users { get; set; } = null!;
        public DbSet<ProjectEntity> Projects { get; set; } = null!;
        public DbSet<ResourceRecordEntity> ResourceRecords { get; set; } = null!;
        public DbSet<FileRecordEntity> FileRecords { get; set; } = null!;
        public DbSet<Models.Workspaces.WorkspaceEntity> Workspaces { get; set; } = null!;

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

        // -----------------------------------------------------------------------------------------////////////////
        //redis patch
        public DbSet<KeyValueEntity> KeyValues { get; set; } = null!;
        public DbSet<SetEntity> Sets { get; set; } = null!;
        public DbSet<PubSubMessageEntity> PubSubMessages { get; set; } = null!;

        // -----------------------------------------------------------------------------------------////////////////
        // comms

        //public DbSet<UserEntity> Users => Set<UserEntity>();
        //public DbSet<WorkspaceEntity> Workspaces => Set<WorkspaceEntity>();
        public DbSet<RoomEntity> Rooms => Set<RoomEntity>();
        public DbSet<RoomMemberEntity> RoomMembers => Set<RoomMemberEntity>();
        public DbSet<MessageEntity> Messages => Set<MessageEntity>();
        public DbSet<MessageAttachmentEntity> MessageAttachments => Set<MessageAttachmentEntity>();
        public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
        public DbSet<CommAuditEntity> CommAudit => Set<CommAuditEntity>();
        public DbSet<DeadLetterEntity> DeadLetters => Set<DeadLetterEntity>();
        public DbSet<TemplateEntity> Templates => Set<TemplateEntity>();
        public DbSet<PresenceEventEntity> PresenceEvents => Set<PresenceEventEntity>();


        // -----------------------------------------------------------------------------------------////////////////

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // converters
            var dtToDto = new ValueConverter<DateTime, DateTimeOffset>(
                d => new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc), TimeSpan.Zero),
                dto => dto.UtcDateTime);

            var nullableDtToDto = new ValueConverter<DateTime?, DateTimeOffset?>(
                d => d.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(d.Value, DateTimeKind.Utc), TimeSpan.Zero) : (DateTimeOffset?)null,
                dto => dto.HasValue ? dto.Value.UtcDateTime : (DateTime?)null);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                    {
                        modelBuilder.Entity(entityType.ClrType)
                            .Property(property.Name)
                            .HasConversion(dtToDto)
                            .HasColumnType("datetimeoffset");
                    }
                    else if (property.ClrType == typeof(DateTime?))
                    {
                        modelBuilder.Entity(entityType.ClrType)
                            .Property(property.Name)
                            .HasConversion(nullableDtToDto)
                            .HasColumnType("datetimeoffset");
                    }
                }
            }

            //---------------------------------------------------/////////////////////////////

            // converters (explicit null for optional JsonSerializerOptions)
            // Converter: Dictionary<string,string>? <-> string?
            var dictConverter = new ValueConverter<Dictionary<string, string>?, string?>(
                dict => dict == null ? null : JsonSerializer.Serialize(dict, (JsonSerializerOptions?)null),
                json => string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null)
            );

            // Comparer for change tracking
            var dictComparer = new ValueComparer<Dictionary<string, string>?>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)
            );

            modelBuilder.Entity<NotificationEntity>(b =>
            {
                b.Property(e => e.Metadata)
                 .HasConversion(dictConverter)
                 .Metadata.SetValueComparer(dictComparer);

                // SQL Server
                b.Property(e => e.Metadata).HasColumnType("nvarchar(max)");

                // If using PostgreSQL (Npgsql), use:
                // b.Property(e => e.Metadata).HasColumnType("jsonb");
            });

            //---------------------------------------------------/////////////////////////////

            //modelBuilder.Entity<KeyValueEntity>().HasKey(k => k.Id);
            //modelBuilder.Entity<SetEntity>().HasKey(s => s.Id);
            //modelBuilder.Entity<PubSubMessageEntity>().HasKey(p => p.Id);

            //modelBuilder.Entity<PubSubMessageEntity>()
            //    .HasIndex(p => new { p.Channel, p.CreatedAtUtc });

            modelBuilder.ApplyConfiguration(new DbKeyValueEntityConfiguration());
            modelBuilder.ApplyConfiguration(new DbSetEntityConfiguration());
            modelBuilder.ApplyConfiguration(new DbPubSubMessageEntityConfiguration());


            //---------------------------------------------------/////////////////////////////

            base.OnModelCreating(modelBuilder);

            //---------------------------------------------------/////////////////////////////

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

            // Comms
            modelBuilder.ApplyConfiguration(new RoomEntityConfiguration());
            modelBuilder.ApplyConfiguration(new RoomMemberEntityConfiguration());
            modelBuilder.ApplyConfiguration(new MessageEntityConfiguration());
            modelBuilder.ApplyConfiguration(new MessageAttachmentEntityConfiguration());
            modelBuilder.ApplyConfiguration(new NotificationEntityConfiguration());
            modelBuilder.ApplyConfiguration(new CommAuditEntityConfiguration());
            modelBuilder.ApplyConfiguration(new DeadLetterEntityConfiguration());
            modelBuilder.ApplyConfiguration(new TemplateEntityConfiguration());
            modelBuilder.ApplyConfiguration(new PresenceEventEntityConfiguration());


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
