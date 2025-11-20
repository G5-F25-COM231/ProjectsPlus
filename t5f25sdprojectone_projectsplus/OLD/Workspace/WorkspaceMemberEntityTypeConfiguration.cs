// Configurations/Workspace/WorkspaceMemberEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace
{
    /// <summary>
    /// EF Core configuration for WorkspaceMember.
    /// - Table "workspace_members"
    /// - Unique(workspace_id, user_id)
    /// - Index on role and workspace_id for common queries
    /// - FKs to workspaces and users with DeleteBehavior.Restrict
    /// - Version as concurrency token
    /// </summary>
    public class WorkspaceMemberEntityTypeConfiguration : IEntityTypeConfiguration<WorkspaceMember>
    {
        public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
        {
            builder.ToTable("workspace_members");

            builder.HasKey(wm => wm.Id);
            builder.Property(wm => wm.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(WorkspaceMember.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_workspace_member_system_type_id_system_types");

            builder.Property(wm => wm.WorkspaceId)
                   .HasColumnName("workspace_id")
                   .IsRequired();

            builder.Property(wm => wm.UserId)
                   .HasColumnName("user_id")
                   .IsRequired();

            builder.Property(wm => wm.Role)
                   .HasColumnName("role")
                   .IsRequired();

            builder.Property(wm => wm.JoinedAt)
                   .HasColumnName("joined_at")
                   .IsRequired(false);

            builder.Property(wm => wm.RemovedAt)
                   .HasColumnName("removed_at")
                   .IsRequired(false);

            builder.Property(wm => wm.InviteToken)
                   .HasColumnName("invite_token")
                   .HasMaxLength(200)
                   .IsRequired(false);

            builder.Property(wm => wm.InviteExpiresAt)
                   .HasColumnName("invite_expires_at")
                   .IsRequired(false);

            builder.Property(wm => wm.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(wm => wm.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(wm => wm.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(wm => new { wm.WorkspaceId, wm.UserId })
                   .IsUnique()
                   .HasDatabaseName("ux_workspace_members_workspace_id_user_id");

            builder.HasIndex(wm => wm.WorkspaceId)
                   .HasDatabaseName("ix_workspace_members_workspace_id");

            builder.HasIndex(wm => wm.Role)
                   .HasDatabaseName("ix_workspace_members_role");

            // FK to workspaces
            builder.HasOne<Workspace>()
                   .WithMany()
                   .HasForeignKey(nameof(WorkspaceMember.WorkspaceId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_workspace_members_workspace_id_workspaces");

            // FK to users
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(WorkspaceMember.UserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_workspace_members_user_id_users");
        }
    }
}
