using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace t5f25sdprojectone_projectsplus.Models
{
    public class ProjectTask
    {
        public long Id { get; set; }

        // FK to Project
        public long ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        // Optional assignee via RoleAssignment
        public long? RoleAssignmentId { get; set; }
        public RoleAssignment? RoleAssignment { get; set; }

        // Core fields
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public byte Status { get; set; } // 0=Pending,1=InProgress,2=Blocked,3=Completed
        public decimal? EstimateHours { get; set; }
        public bool IsDone { get; set; } = false;
        public DateTimeOffset? DueDate { get; set; }

        // Audit
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    public class ProjectTaskEntityTypeConfiguration : IEntityTypeConfiguration<ProjectTask>
    {
        public void Configure(EntityTypeBuilder<ProjectTask> builder)
        {
            builder.ToTable("ProjectTasks");

            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedOnAdd();

            builder.Property(t => t.ProjectId)
                   .HasColumnType("bigint")
                   .IsRequired();

            builder.HasOne(t => t.Project)
                   .WithMany(p => p.Tasks)
                   .HasForeignKey(t => t.ProjectId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Property(t => t.RoleAssignmentId)
                   .HasColumnType("bigint");

            builder.HasOne(t => t.RoleAssignment)
                   .WithMany()
                   .HasForeignKey(t => t.RoleAssignmentId)
                   .OnDelete(DeleteBehavior.SetNull);

            builder.Property(t => t.Title)
                   .HasColumnType("nvarchar(300)")
                   .IsRequired();

            builder.Property(t => t.Description)
                   .HasColumnType("nvarchar(max)");

            builder.Property(t => t.Status)
                   .HasColumnType("tinyint")
                   .IsRequired();

            builder.Property(t => t.EstimateHours)
                   .HasColumnType("decimal(6,2)");

            builder.Property(t => t.DueDate)
                   .HasColumnType("datetime2");

            builder.Property(t => t.CreatedAt)
                   .HasColumnType("datetime2")
                   .HasDefaultValueSql("SYSUTCDATETIME()")
                   .IsRequired();

            builder.Property(t => t.UpdatedAt)
                   .HasColumnType("datetime2")
                   .HasDefaultValueSql("SYSUTCDATETIME()")
                   .IsRequired();

            builder.HasIndex(t => new { t.ProjectId, t.Status })
                   .HasDatabaseName("IX_ProjectTasks_Project_Status");
        }
    }
}
