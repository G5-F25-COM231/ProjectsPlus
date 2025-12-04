// src/ProjectsPlus.Data/Configurations/CommsEntityConfigurations.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Communication;

// Configure conversions for JSON-like properties if you want to map to POCOs in the future.
// Example converters can be added in repository/service layer when mapping DTOs <-> Entities.

namespace t5f25sdprojectone_projectsplus.Data.Configurations.Communication
{
    // Room
    public class RoomEntityConfiguration : IEntityTypeConfiguration<RoomEntity>
    {
        public void Configure(EntityTypeBuilder<RoomEntity> builder)
        {
            builder.ToTable("Rooms");

            builder.HasKey(x => x.RoomId);

            builder.Property(x => x.RoomId).HasColumnName("room_id");
            builder.Property(x => x.WorkspaceId).HasColumnName("workspace_id");
            builder.Property(x => x.Name).IsRequired().HasMaxLength(256).HasColumnName("name");
            builder.Property(x => x.IsPrivate).HasDefaultValue(true).HasColumnName("is_private");
            builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)").HasColumnName("metadata_json");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");

            builder.HasIndex(x => x.WorkspaceId).HasDatabaseName("IX_rooms_workspace_id");

            // Seeds (3) - GUIDs for RoomId and WorkspaceId
            builder.HasData(
                new RoomEntity
                {
                    RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    WorkspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Name = "General",
                    IsPrivate = false,
                    MetadataJson = "{\"purpose\":\"general chat\"}",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 0, 0, 0), DateTimeKind.Utc)
                },
                new RoomEntity
                {
                    RoomId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    WorkspaceId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Name = "Announcements",
                    IsPrivate = true,
                    MetadataJson = "{\"purpose\":\"workspace announcements\"}",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 1, 0, 0, 0), DateTimeKind.Utc)
                },
                new RoomEntity
                {
                    RoomId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    WorkspaceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Name = "Engineering",
                    IsPrivate = false,
                    MetadataJson = "{\"purpose\":\"engineering discussions\"}",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 1, 0, 0, 0), DateTimeKind.Utc)
                }
            );
        }
    }

    // RoomMember
    public class RoomMemberEntityConfiguration : IEntityTypeConfiguration<RoomMemberEntity>
    {
        public void Configure(EntityTypeBuilder<RoomMemberEntity> builder)
        {
            builder.ToTable("RoomMembers");

            builder.HasKey(x => new { x.RoomId, x.UserId });

            builder.Property(x => x.RoomId).HasColumnName("room_id");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.Property(x => x.Role).HasMaxLength(64).HasDefaultValue("member").HasColumnName("role");
            builder.Property(x => x.JoinedAt).HasColumnType("datetimeoffset").HasColumnName("joined_at");

            builder.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            builder.HasIndex(x => x.UserId).HasDatabaseName("IX_roommembers_user_id");

            // Seeds (3) - UserId are numeric (long) and RoomId are GUIDs
            builder.HasData(
                new RoomMemberEntity
                {
                    RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    UserId = 1001,
                    Role = "owner",
                    JoinedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1), DateTimeKind.Utc)
                },
                new RoomMemberEntity
                {
                    RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    UserId = 1002,
                    Role = "member",
                    JoinedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 2), DateTimeKind.Utc)
                },
                new RoomMemberEntity
                {
                    RoomId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    UserId = 1003,
                    Role = "member",
                    JoinedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 2), DateTimeKind.Utc)
                }
            );

        }
    }

    // Message
    public class MessageEntityConfiguration : IEntityTypeConfiguration<MessageEntity>
    {
        public void Configure(EntityTypeBuilder<MessageEntity> builder)
        {
            builder.ToTable("Messages");

            builder.HasKey(x => x.MessageId);

            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.RoomId).HasColumnName("room_id");
            builder.Property(x => x.ThreadRootId).HasColumnName("thread_root_id");
            builder.Property(x => x.SenderUserId).HasColumnName("sender_user_id");
            builder.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id");
            builder.Property(x => x.Body).HasColumnType("nvarchar(max)").HasColumnName("body");
            builder.Property(x => x.BodyHtml).HasColumnType("nvarchar(max)").HasColumnName("body_html");
            builder.Property(x => x.Snippet).HasMaxLength(4000).HasColumnName("snippet");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");
            builder.Property(x => x.EditedAt).HasColumnType("datetimeoffset").HasColumnName("edited_at");
            builder.Property(x => x.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
            builder.Property(x => x.Visibility).HasMaxLength(64).HasDefaultValue("visible").HasColumnName("visibility");
            builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)").HasColumnName("metadata_json");

            builder.HasIndex(x => new { x.RoomId, x.CreatedAt }).HasDatabaseName("IX_messages_room_created_at");
            builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt }).HasDatabaseName("IX_messages_recipient_created_at");
            builder.HasIndex(x => x.SenderUserId).HasDatabaseName("IX_messages_sender_user_id");

            // Seeds (3) - MessageId and RoomId are GUIDs; SenderUserId/RecipientUserId are long
            builder.HasData(
                 new MessageEntity
                 {
                     MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                     RoomId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                     ThreadRootId = null,
                     SenderUserId = 1001,
                     RecipientUserId = null,
                     Body = "Welcome to the General room.",
                     BodyHtml = "<p>Welcome to the <strong>General</strong> room.</p>",
                     Snippet = "Welcome to the General room.",
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 12, 0, 0), DateTimeKind.Utc),
                     IsDeleted = false,
                     Visibility = "visible",
                     MetadataJson = "{\"pinned\":false}"
                 },
                 new MessageEntity
                 {
                     MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                     RoomId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                     ThreadRootId = null,
                     SenderUserId = 1002,
                     RecipientUserId = null,
                     Body = "Announcement: maintenance window tomorrow.",
                     BodyHtml = "<p><em>Announcement:</em> maintenance window tomorrow.</p>",
                     Snippet = "Announcement: maintenance window tomorrow.",
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 10, 9, 30, 0), DateTimeKind.Utc),
                     IsDeleted = false,
                     Visibility = "visible",
                     MetadataJson = "{\"announcement\":true}"
                 },
                 new MessageEntity
                 {
                     MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
                     RoomId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                     ThreadRootId = null,
                     SenderUserId = 1003,
                     RecipientUserId = null,
                     Body = "Sprint planning notes attached.",
                     BodyHtml = "<p>Sprint planning notes attached.</p>",
                     Snippet = "Sprint planning notes attached.",
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 5, 14, 0, 0), DateTimeKind.Utc),
                     IsDeleted = false,
                     Visibility = "visible",
                     MetadataJson = "{\"tags\":[\"planning\",\"sprint\"]}"
                 }
             );

        }
    }

    // MessageAttachment
    public class MessageAttachmentEntityConfiguration : IEntityTypeConfiguration<MessageAttachmentEntity>
    {
        public void Configure(EntityTypeBuilder<MessageAttachmentEntity> builder)
        {
            builder.ToTable("MessageAttachments");

            builder.HasKey(x => x.AttachmentId);

            builder.Property(x => x.AttachmentId).HasColumnName("attachment_id");
            builder.Property(x => x.MessageId).HasColumnName("message_id");
            builder.Property(x => x.Filename).HasMaxLength(512).HasColumnName("filename");
            builder.Property(x => x.ContentType).HasMaxLength(256).HasColumnName("content_type");
            builder.Property(x => x.StoragePointer).HasMaxLength(1024).HasColumnName("storage_pointer");
            builder.Property(x => x.DdbId).HasMaxLength(128).HasColumnName("ddb_id");
            builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            builder.Property(x => x.UploaderUserId).HasColumnName("uploader_user_id");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");

            builder.HasIndex(x => x.MessageId).HasDatabaseName("IX_messageattachments_message_id");

            // Seeds (3) - AttachmentId and MessageId are GUIDs; UploaderUserId is long
            builder.HasData(
                new MessageAttachmentEntity
                {
                    AttachmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
                    MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
                    Filename = "welcome.pdf",
                    ContentType = "application/pdf",
                    StoragePointer = "s3://projectsplus-attachments/welcome.pdf",
                    DdbId = "att-0001",
                    SizeBytes = 102400,
                    UploaderUserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 12, 1, 0), DateTimeKind.Utc)
                },
                new MessageAttachmentEntity
                {
                    AttachmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
                    MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"),
                    Filename = "maintenance-details.txt",
                    ContentType = "text/plain",
                    StoragePointer = "s3://projectsplus-attachments/maintenance-details.txt",
                    DdbId = "att-0002",
                    SizeBytes = 2048,
                    UploaderUserId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 10, 9, 31, 0), DateTimeKind.Utc)
                },
                new MessageAttachmentEntity
                {
                    AttachmentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"),
                    MessageId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
                    Filename = "sprint-plan.xlsx",
                    ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    StoragePointer = "s3://projectsplus-attachments/sprint-plan.xlsx",
                    DdbId = "att-0003",
                    SizeBytes = 512000,
                    UploaderUserId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 5, 14, 1, 0), DateTimeKind.Utc)
                }
            );

        }
    }

    // Notification (durable queue)
    public class NotificationEntityConfiguration : IEntityTypeConfiguration<NotificationEntity>
    {
        public void Configure(EntityTypeBuilder<NotificationEntity> builder)
        {
            builder.ToTable("Notifications");

            builder.HasKey(x => x.NotificationId);

            builder.Property(x => x.NotificationId).HasColumnName("notification_id");
            builder.Property(x => x.Channel).HasMaxLength(64).IsRequired().HasColumnName("channel");
            builder.Property(x => x.Recipient).HasMaxLength(512).IsRequired().HasColumnName("recipient");
            builder.Property(x => x.Subject).HasMaxLength(512).HasColumnName("subject");
            builder.Property(x => x.Body).HasColumnType("nvarchar(max)").HasColumnName("body");
            builder.Property(x => x.VariablesJson).HasColumnType("nvarchar(max)").HasColumnName("variables_json");
            builder.Property(x => x.Priority).HasDefaultValue((byte)1).HasColumnName("priority");
            builder.Property(x => x.Status).HasMaxLength(64).HasDefaultValue("pending").HasColumnName("status");
            builder.Property(x => x.Attempts).HasDefaultValue(0).HasColumnName("attempts");
            builder.Property(x => x.ScheduledFor).HasColumnType("datetimeoffset").HasColumnName("scheduled_for");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");
            builder.Property(x => x.LastAttemptAt).HasColumnType("datetimeoffset").HasColumnName("last_attempt_at");
            builder.Property(x => x.ProviderMessageId).HasMaxLength(256).HasColumnName("provider_message_id");
            builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)").HasColumnName("metadata_json");

            builder.HasIndex(x => new { x.Status, x.ScheduledFor }).HasDatabaseName("IX_notifications_status_scheduled");
            builder.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_notifications_created_at");

            // Seeds (3) - NotificationId are GUIDs
            builder.HasData(
                 new NotificationEntity
                 {
                     NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
                     Channel = "Email",
                     Recipient = Guid.Parse("00000000-0000-0000-0000-000000000001").ToString(),
                     Subject = "Welcome to ProjectsPlus",
                     Body = "Welcome! This is your first notification.",
                     VariablesJson = "{\"userId\":\"00000000-0000-0000-0000-000000000001\"}",
                     Priority = 1,
                     Status = "pending",
                     Attempts = 0,
                     ScheduledFor = null,
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 12, 2, 0), DateTimeKind.Utc),
                     MetadataJson = "{\"source\":\"system\"}"
                 },
                 new NotificationEntity
                 {
                     NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
                     Channel = "Webhook",
                     Recipient = "https://hooks.example.com/notify",
                     Subject = null,
                     Body = "{\"event\":\"maintenance\",\"when\":\"2025-02-11T02:00:00Z\"}",
                     VariablesJson = "{\"workspaceId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"}",
                     Priority = 2,
                     Status = "pending",
                     Attempts = 0,
                     ScheduledFor = DateTime.SpecifyKind(new DateTime(2025, 2, 10, 9, 0, 0), DateTimeKind.Utc),
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 9, 12, 0, 0), DateTimeKind.Utc),
                     MetadataJson = "{\"retryPolicy\":\"exponential\"}"
                 },
                 new NotificationEntity
                 {
                     NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000003"),
                     Channel = "InApp",
                     Recipient = Guid.Parse("00000000-0000-0000-0000-000000000003").ToString(),
                     Subject = "Sprint Reminder",
                     Body = "Don't forget sprint planning at 15:00 UTC.",
                     VariablesJson = "{\"roomId\":\"33333333-3333-3333-3333-333333333333\"}",
                     Priority = 1,
                     Status = "pending",
                     Attempts = 0,
                     ScheduledFor = null,
                     CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 5, 13, 0, 0), DateTimeKind.Utc),
                     MetadataJson = "{\"source\":\"scheduler\"}"
                 }
             );

        }
    }

    // CommAudit
    public class CommAuditEntityConfiguration : IEntityTypeConfiguration<CommAuditEntity>
    {
        public void Configure(EntityTypeBuilder<CommAuditEntity> builder)
        {
            builder.ToTable("CommAudit");

            builder.HasKey(x => x.AuditId);

            builder.Property(x => x.AuditId).HasColumnName("audit_id");
            builder.Property(x => x.NotificationId).HasColumnName("notification_id");
            builder.Property(x => x.Channel).HasMaxLength(64).HasColumnName("channel");
            builder.Property(x => x.Recipient).HasMaxLength(512).HasColumnName("recipient");
            builder.Property(x => x.Status).HasMaxLength(64).HasColumnName("status");
            builder.Property(x => x.ProviderMessageId).HasMaxLength(256).HasColumnName("provider_message_id");
            builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)").HasColumnName("error_message");
            builder.Property(x => x.Timestamp).HasColumnType("datetimeoffset").HasColumnName("timestamp");
            builder.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").HasColumnName("payload_json");

            builder.HasIndex(x => x.NotificationId).HasDatabaseName("IX_commaudit_notification_id");
            builder.HasIndex(x => x.Timestamp).HasDatabaseName("IX_commaudit_timestamp");

            // Seeds (3) - AuditId and NotificationId are GUIDs
            builder.HasData(
                new CommAuditEntity
                {
                    AuditId = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
                    Channel = "Email",
                    Recipient = "user1@example.edu",
                    Status = "Sent",
                    ProviderMessageId = "prov-msg-1001",
                    ErrorMessage = null,
                    Timestamp = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 12, 3, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"attempt\":1}"
                },
                new CommAuditEntity
                {
                    AuditId = Guid.Parse("dddddddd-0000-0000-0000-000000000002"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
                    Channel = "Webhook",
                    Recipient = "https://hooks.example.com/notify",
                    Status = "Failed",
                    ProviderMessageId = null,
                    ErrorMessage = "Timeout connecting to webhook endpoint",
                    Timestamp = DateTime.SpecifyKind(new DateTime(2025, 2, 10, 9, 1, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"attempt\":1}"
                },
                new CommAuditEntity
                {
                    AuditId = Guid.Parse("dddddddd-0000-0000-0000-000000000003"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000003"),
                    Channel = "InApp",
                    Recipient = "1002",
                    Status = "Delivered",
                    ProviderMessageId = null,
                    ErrorMessage = null,
                    Timestamp = DateTime.SpecifyKind(new DateTime(2025, 3, 5, 13, 5, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"deliveredToConnections\":1}"
                }
            );
        }
    }

    // DeadLetter
    public class DeadLetterEntityConfiguration : IEntityTypeConfiguration<DeadLetterEntity>
    {
        public void Configure(EntityTypeBuilder<DeadLetterEntity> builder)
        {
            builder.ToTable("DeadLetters");

            builder.HasKey(x => x.DeadId);

            builder.Property(x => x.DeadId).HasColumnName("dead_id");
            builder.Property(x => x.NotificationId).HasColumnName("notification_id");
            builder.Property(x => x.Reason).HasMaxLength(1024).HasColumnName("reason");
            builder.Property(x => x.Attempts).HasColumnName("attempts");
            builder.Property(x => x.LastError).HasColumnType("nvarchar(max)").HasColumnName("last_error");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");
            builder.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)").HasColumnName("payload_json");

            builder.HasIndex(x => x.NotificationId).HasDatabaseName("IX_deadletters_notification_id");

            // Seeds (3) - DeadId and NotificationId are GUIDs
            builder.HasData(
                new DeadLetterEntity
                {
                    DeadId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000002"),
                    Reason = "Webhook unreachable after retries",
                    Attempts = 5,
                    LastError = "DNS resolution failed",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 10, 10, 0, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"url\":\"https://hooks.example.com/notify\"}"
                },
                new DeadLetterEntity
                {
                    DeadId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000004"),
                    Reason = "Invalid recipient",
                    Attempts = 3,
                    LastError = "400 Bad Request",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 4, 1, 8, 0, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"recipient\":\"invalid@example\"}"
                },
                new DeadLetterEntity
                {
                    DeadId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003"),
                    NotificationId = Guid.Parse("cccccccc-0000-0000-0000-000000000005"),
                    Reason = "Provider rejected message",
                    Attempts = 4,
                    LastError = "Provider error code 502",
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 5, 2, 9, 30, 0), DateTimeKind.Utc),
                    PayloadJson = "{\"provider\":\"sms-gateway\"}"
                }
            );
        }
    }

    // Template
    public class TemplateEntityConfiguration : IEntityTypeConfiguration<TemplateEntity>
    {
        public void Configure(EntityTypeBuilder<TemplateEntity> builder)
        {
            builder.ToTable("Templates");

            builder.HasKey(x => x.TemplateId);

            builder.Property(x => x.TemplateId).HasColumnName("template_id");
            builder.Property(x => x.Name).IsRequired().HasMaxLength(256).HasColumnName("name");
            builder.Property(x => x.Scope).HasMaxLength(64).HasColumnName("scope");
            builder.Property(x => x.ScopeKey).HasMaxLength(128).HasColumnName("scope_key");
            builder.Property(x => x.Format).HasMaxLength(32).HasColumnName("format");
            builder.Property(x => x.SubjectTemplate).HasColumnType("nvarchar(max)").HasColumnName("subject_template");
            builder.Property(x => x.BodyTemplate).HasColumnType("nvarchar(max)").HasColumnName("body_template");
            builder.Property(x => x.Version).HasColumnName("version");
            builder.Property(x => x.IsActive).HasColumnName("is_active");
            builder.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").HasColumnName("created_at");
            builder.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset").HasColumnName("updated_at");

            builder.HasIndex(x => new { x.Scope, x.ScopeKey }).HasDatabaseName("IX_templates_scope_scopekey");
            builder.HasIndex(x => x.IsActive).HasDatabaseName("IX_templates_is_active");

            // Seeds (3) - TemplateId strings
            builder.HasData(
                new TemplateEntity
                {
                    TemplateId = "tmpl-welcome-001",
                    Name = "Welcome Email",
                    Scope = "Global",
                    ScopeKey = null,
                    Format = "Html",
                    SubjectTemplate = "Welcome to ProjectsPlus, {{displayName}}",
                    BodyTemplate = "<p>Hi {{displayName}}, welcome to ProjectsPlus.</p>",
                    Version = 1,
                    IsActive = true,
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 1, 1, 0, 0, 0), DateTimeKind.Utc)
                },
                new TemplateEntity
                {
                    TemplateId = "tmpl-announcement-001",
                    Name = "Workspace Announcement",
                    Scope = "Workspace",
                    ScopeKey = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    Format = "Html",
                    SubjectTemplate = "Announcement: {{title}}",
                    BodyTemplate = "<p>{{body}}</p>",
                    Version = 1,
                    IsActive = true,
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 2, 1, 0, 0, 0), DateTimeKind.Utc)
                },
                new TemplateEntity
                {
                    TemplateId = "tmpl-inapp-001",
                    Name = "InApp Notification",
                    Scope = "Global",
                    ScopeKey = null,
                    Format = "Text",
                    SubjectTemplate = "",
                    BodyTemplate = "You have a new message from {{sender}}",
                    Version = 1,
                    IsActive = true,
                    CreatedAt = DateTime.SpecifyKind(new DateTime(2025, 3, 1, 0, 0, 0), DateTimeKind.Utc)
                }
            );
        }
    }

    // PresenceEvent
    public class PresenceEventEntityConfiguration : IEntityTypeConfiguration<PresenceEventEntity>
    {
        public void Configure(EntityTypeBuilder<PresenceEventEntity> builder)
        {
            builder.ToTable("PresenceEvents");

            builder.HasKey(x => x.PresenceEventId);

            builder.Property(x => x.PresenceEventId).HasColumnName("presence_event_id");
            builder.Property(x => x.UserId).HasColumnName("user_id");
            builder.Property(x => x.Status).IsRequired().HasMaxLength(64).HasColumnName("status");
            builder.Property(x => x.TimestampUtc).HasColumnType("datetimeoffset").HasColumnName("timestamp_utc");
            builder.Property(x => x.ConnectionId).HasMaxLength(256).HasColumnName("connection_id");
            builder.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)").HasColumnName("metadata_json");

            builder.HasIndex(x => x.UserId).HasDatabaseName("IX_presenceevents_user_id");
            builder.HasIndex(x => x.TimestampUtc).HasDatabaseName("IX_presenceevents_timestamp");

            // Seeds (3) - PresenceEventId GUIDs, UserId numeric
            builder.HasData(
                new PresenceEventEntity
                {
                    PresenceEventId = Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
                    UserId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Status = "Online",
                    TimestampUtc = DateTime.SpecifyKind(new DateTime(2025, 6, 1, 12, 0, 0), DateTimeKind.Utc),
                    ConnectionId = "conn-1000-1",
                    MetadataJson = "{\"ip\":\"192.0.2.1\"}"
                },
                new PresenceEventEntity
                {
                    PresenceEventId = Guid.Parse("ffffffff-0000-0000-0000-000000000002"),
                    UserId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    Status = "Away",
                    TimestampUtc = DateTime.SpecifyKind(new DateTime(2025, 6, 1, 12, 5, 0), DateTimeKind.Utc),
                    ConnectionId = "conn-1001-1",
                    MetadataJson = "{\"platform\":\"web\"}"
                },
                new PresenceEventEntity
                {
                    PresenceEventId = Guid.Parse("ffffffff-0000-0000-0000-000000000003"),
                    UserId = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    Status = "Offline",
                    TimestampUtc = DateTime.SpecifyKind(new DateTime(2025, 6, 1, 11, 50, 0), DateTimeKind.Utc),
                    ConnectionId = null,
                    MetadataJson = "{\"reason\":\"manual-signout\"}"
                }
            );

        }
    }
}
