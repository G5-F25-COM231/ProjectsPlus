// src/ProjectsPlus.Comms/Persistence/CommsDbContext.cs
// EF Core DbContext and entity models for ProjectsPlus communications subsystem.
// Targets MS SQL Server Express via EF Core migrations.
// Note: keep entities small and focused; use JSON string columns for flexible metadata.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Models.Workspaces;

namespace t5f25sdprojectone_projectsplus.Models.Communication
{
    #region Entities

    
    public sealed class RoomEntity
    {
        public Guid RoomId { get; set; }
        public Guid? WorkspaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsPrivate { get; set; } = true;
        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? MetadataJson { get; set; }
    }

    public sealed class RoomMemberEntity
    {
        public Guid RoomId { get; set; }
        public Guid UserId { get; set; }
        public string Role { get; set; } = "member";
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation convenience (not required)
        public RoomEntity? Room { get; set; }
        public UserEntity? User { get; set; }
    }

    public sealed class MessageEntity
    {
        public Guid MessageId { get; set; }
        public Guid? RoomId { get; set; }
        public Guid? ThreadRootId { get; set; }
        public Guid? SenderUserId { get; set; }
        public Guid? RecipientUserId { get; set; } // for DMs
        public string? Body { get; set; }
        public string? BodyHtml { get; set; }
        public string? Snippet { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; } = false;
        public string? Visibility { get; set; } = "visible";
        public string? MetadataJson { get; set; }
    }

    public sealed class MessageAttachmentEntity
    {
        public Guid AttachmentId { get; set; }
        public Guid? MessageId { get; set; }
        public string? Filename { get; set; }
        public string? ContentType { get; set; }
        public string? StoragePointer { get; set; } // s3://bucket/key
        public string? DdbId { get; set; } // optional link to DynamoDB item Id
        public long? SizeBytes { get; set; }
        public Guid? UploaderUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class NotificationEntity
    {
        public Guid NotificationId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? VariablesJson { get; set; } // JSON
        public byte Priority { get; set; } = 1;
        public string Status { get; set; } = "pending";
        public int Attempts { get; set; } = 0;
        public DateTime? ScheduledFor { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastAttemptAt { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? MetadataJson { get; set; }
        public string? LastError { get; internal set; }
        public DateTime SentAt { get; internal set; }
        public Dictionary<string, string> Metadata { get; internal set; }
    }

    public sealed class CommAuditEntity
    {
        public Guid AuditId { get; set; }
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public string? Recipient { get; set; }
        public string? Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? PayloadJson { get; set; }
        public string? EventType { get; internal set; }
        public string? CorrelationId { get; internal set; }
    }

    public sealed class DeadLetterEntity
    {
        public Guid DeadId { get; set; }
        public Guid? NotificationId { get; set; }
        public string? Reason { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? PayloadJson { get; set; }
    }

    public sealed class TemplateEntity
    {
        public string TemplateId { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; } = string.Empty;
        public string Scope { get; set; } = "Global";
        public string? ScopeKey { get; set; }
        public string Format { get; set; } = "Html";
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class PresenceEventEntity
    {
        public Guid PresenceEventId { get; set; }
        public Guid UserId { get; set; }
        public string Status { get; set; } = "Offline";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string? ConnectionId { get; set; }
        public string? MetadataJson { get; set; }
    }

    #endregion
}
