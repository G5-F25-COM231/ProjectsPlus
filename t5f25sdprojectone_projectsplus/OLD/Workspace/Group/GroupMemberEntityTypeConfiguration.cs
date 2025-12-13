// Configurations/Workspace/GroupMemberEntityTypeConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.SystemTypeID;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.OLD.Workspace.Group
{
    /// <summary>
    /// EF Core configuration for GroupMember.
    /// - Table "group_members"
    /// - Unique(group_id, user_id)
    /// - Index on group_id and role for queries
    /// - FKs to groups and users with DeleteBehavior.Restrict
    /// - Version as concurrency token
    /// </summary>
    public class GroupMemberEntityTypeConfiguration : IEntityTypeConfiguration<GroupMember>
    {
        public void Configure(EntityTypeBuilder<GroupMember> builder)
        {
            builder.ToTable("group_members");

            builder.HasKey(gm => gm.Id);
            builder.Property(gm => gm.Id)
                   .HasColumnName("id")
                   .ValueGeneratedOnAdd();

            builder.Property(f => f.SystemTypeId).HasColumnName("system_type_id").IsRequired();
            builder.HasOne<SystemType>()
                   .WithMany()
                   .HasForeignKey(nameof(GroupMember.SystemTypeId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_group_member_system_type_id_system_types");

            builder.Property(gm => gm.GroupId)
                   .HasColumnName("group_id")
                   .IsRequired();

            builder.Property(gm => gm.UserId)
                   .HasColumnName("user_id")
                   .IsRequired();

            builder.Property(gm => gm.Role)
                   .HasColumnName("role")
                   .IsRequired();

            builder.Property(gm => gm.JoinedAt)
                   .HasColumnName("joined_at")
                   .IsRequired(false);

            builder.Property(gm => gm.RemovedAt)
                   .HasColumnName("removed_at")
                   .IsRequired(false);

            builder.Property(gm => gm.CreatedAt)
                   .HasColumnName("created_at")
                   .IsRequired();

            builder.Property(gm => gm.UpdatedAt)
                   .HasColumnName("updated_at")
                   .IsRequired();

            builder.Property(gm => gm.Version)
                   .HasColumnName("version")
                   .IsRequired()
                   .IsConcurrencyToken();

            builder.HasIndex(gm => new { gm.GroupId, gm.UserId })
                   .IsUnique()
                   .HasDatabaseName("ux_group_members_group_id_user_id");

            builder.HasIndex(gm => gm.GroupId)
                   .HasDatabaseName("ix_group_members_group_id");

            builder.HasIndex(gm => gm.Role)
                   .HasDatabaseName("ix_group_members_role");

            // FK to groups
            builder.HasOne<Group>()
                   .WithMany()
                   .HasForeignKey(nameof(GroupMember.GroupId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_group_members_group_id_groups");

            // FK to users
            builder.HasOne<User>()
                   .WithMany()
                   .HasForeignKey(nameof(GroupMember.UserId))
                   .OnDelete(DeleteBehavior.Restrict)
                   .HasConstraintName("fk_group_members_user_id_users");
        }
    }
}
