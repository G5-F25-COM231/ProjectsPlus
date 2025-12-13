// src/ProjectsPlus.Comms/Contracts/Communications.Contracts.cs
// Canonical DTOs, enums, service and repository interfaces for ProjectsPlus communications.
// Designed for EF Core (MS SQL Server Express), S3 for binaries, and DynamoDB for small metadata.
// Namespace aligns with existing project structure.

using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    #region Enums

    public enum ChannelType
    {
        Email,
        Sms,
        Push,
        InApp,
        Webhook
    }

    public enum Priority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    public enum TemplateFormat
    {
        Text,
        Html
    }

    public enum TemplateScope
    {
        Global,
        Workspace,
        Room,
        Project
    }

    public enum SendStatus
    {
        Pending,
        Processing,
        Sent,
        Delivered,
        Failed,
        Retrying,
        Dropped
    }

    public enum PresenceStatus
    {
        Offline,
        Online,
        Away,
        DoNotDisturb
    }

    #endregion

    #region Core DTOs

    public sealed class UserDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // Realtime presence fields
        public DateTime? LastSeenUtc { get; set; }
        public PresenceStatus Presence { get; set; } = PresenceStatus.Offline;

        // Optional: persisted list of connection ids (nullable; prefer Redis for multi-node)
        public IReadOnlyList<string>? ActiveConnectionIds { get; set; }
    }

    public sealed class WorkspaceDto
    {
        public Guid WorkspaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? OwnerUserId { get; set; }
        public IDictionary<string, object?>? Settings { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class RoomDto
    {
        public Guid RoomId { get; set; }
        public Guid? WorkspaceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsPrivate { get; set; } = true;
        public long? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public IDictionary<string, object?>? Metadata { get; set; }
    }

    public sealed class ChatMessageDto
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public Guid? RoomId { get; set; }
        public Guid? ThreadRootId { get; set; }
        public long? SenderUserId { get; set; }
        public long? RecipientUserId { get; set; } // for DMs
        public string? Body { get; set; }
        public string? BodyHtml { get; set; }
        public string? Snippet { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? EditedAt { get; set; }
        public bool IsDeleted { get; set; }
        public IDictionary<string, object?>? Metadata { get; set; }
        public IReadOnlyList<AttachmentDescriptor>? Attachments { get; set; }

    }

    public sealed class AttachmentDescriptor
    {
        public Guid AttachmentId { get; set; } = Guid.NewGuid();
        public string Filename { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long? SizeBytes { get; set; }
        public string StoragePointer { get; set; } = string.Empty; // s3://bucket/key
        public string? DdbId { get; set; } // optional DynamoDB id
        public Guid? MessageId { get; set; }
        public Guid? UploaderUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class NotificationDto
    {
        public Guid NotificationId { get; set; } = Guid.NewGuid();
        public ChannelType Channel { get; set; } = ChannelType.Email;
        public string Recipient { get; set; } = string.Empty; // email, phone, device token, user id, or webhook url
        public string? RecipientDisplayName { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public TemplateReferenceDto? Template { get; set; }
        public IDictionary<string, object?>? Variables { get; set; }
        public Priority Priority { get; set; } = Priority.Normal;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ScheduledFor { get; set; }
        public string? CorrelationId { get; set; } // link to project/job/message
        public IDictionary<string, string>? Metadata { get; set; }
    }

    public sealed class NotificationCreateDto
    {
        public string Channel { get; init; } = string.Empty;
        public string Recipient { get; init; } = string.Empty;
        public string? Subject { get; init; }
        public string Body { get; init; } = string.Empty;
        public string? VariablesJson { get; init; }
        public DateTime? ScheduledForUtc { get; init; }
        public byte Priority { get; init; } = 1;
    }

    public sealed class TemplateReferenceDto
    {
        public string TemplateId { get; set; } = string.Empty;
        public TemplateScope Scope { get; set; } = TemplateScope.Global;
        public string? ScopeKey { get; set; } // e.g., workspace id, room id
    }

    public sealed class TemplateDto
    {
        public string TemplateId { get; set; } = Guid.NewGuid().ToString("D");
        public string Name { get; set; } = string.Empty;
        public TemplateScope Scope { get; set; } = TemplateScope.Global;
        public string? ScopeKey { get; set; }
        public TemplateFormat Format { get; set; } = TemplateFormat.Html;
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;
        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
    

    public sealed class SendResultDto
    {
        public Guid NotificationId { get; set; }
        public SendStatus Status { get; set; } = SendStatus.Pending;
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int Attempt { get; set; } = 0;
    }



    public sealed class CommunicationAuditEntryDto
    {
        public Guid AuditId { get; set; } = Guid.NewGuid();
        public Guid NotificationId { get; set; }
        public ChannelType Channel { get; set; }
        public string Recipient { get; set; } = string.Empty;
        public SendStatus Status { get; set; }
        public string? ProviderMessageId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public IDictionary<string, string>? Metadata { get; set; }
        public string? PayloadJson { get; set; }
    }

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public string? ContinuationToken { get; set; }
        public int TotalCount { get; set; }
    }

    #endregion

    #region Realtime and WebSocket

    //public sealed class WsEnvelope
    //{
    //    public string Type { get; set; } = string.Empty; // "message", "typing", "presence", "ack", "status"
    //    public string? MessageId { get; set; }
    //    public Guid? RoomId { get; set; }
    //    public Guid? ToUserId { get; set; }
    //    public string? Body { get; set; }
    //    public IDictionary<string, object?>? Meta { get; set; }
    //}

    public sealed class PresenceEventDto
    {
        public Guid UserId { get; set; }
        public PresenceStatus Status { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string? ConnectionId { get; set; }
        public IDictionary<string, object?>? Metadata { get; set; }
    }

    #endregion

    #region Service contracts

    // Message center manages inbox/outbox, threading, read/unread, attachments
    public interface IMessageCenterService
    {
        Task<ChatMessageDto> SendDirectMessageAsync(Guid fromUserId, Guid toUserId, string body, IEnumerable<AttachmentDescriptor>? attachments = null, CancellationToken ct = default);
        Task<ChatMessageDto> SaveIncomingMessageAsync(ChatMessageDto message, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetUserInboxAsync(Guid userId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetRoomHistoryAsync(Guid roomId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
        Task MarkAsReadAsync(Guid userId, Guid messageId, CancellationToken ct = default);
        Task DeleteMessageAsync(Guid userId, Guid messageId, bool hardDelete = false, CancellationToken ct = default);
        Task<IReadOnlyList<AttachmentDescriptor>> GetAttachmentsForMessageAsync(Guid messageId, CancellationToken ct = default);
    }

    // Chatroom service manages rooms, membership, posting, moderation
    public interface IChatroomService
    {
        Task<RoomDto> CreateRoomAsync(Guid workspaceId, string name, long createdBy, bool isPrivate = true, CancellationToken ct = default);
        Task JoinRoomAsync(Guid roomId, long userId, CancellationToken ct = default);
        Task LeaveRoomAsync(Guid roomId, long userId, CancellationToken ct = default);
        Task<ChatMessageDto> PostMessageAsync(Guid roomId, long senderUserId, string body, IEnumerable<AttachmentDescriptor>? attachments = null, CancellationToken ct = default);
        Task<IReadOnlyList<ChatMessageDto>> GetRoomMembersAsync(Guid roomId, CancellationToken ct = default);
        Task KickMemberAsync(Guid roomId, long moderatorUserId, long targetUserId, CancellationToken ct = default);

        Task AddConnectionToRoomAsync(string roomId, string connectionId, long? userId);
        Task RemoveConnectionFromRoomAsync(string roomId, string connectionId, long? userId);
        Task HandleRealtimeMessageAsync(RealtimeEnvelope envelope, string connectionId, long? userId, CancellationToken ct = default);
    }

    // Notification center orchestrates templates, channel selection, queueing
    public interface INotificationCenter
    {
        Task<SendResultDto> EnqueueNotificationAsync(NotificationDto notification, CancellationToken ct = default);
        Task<SendResultDto> SendNowAsync(NotificationDto notification, CancellationToken ct = default);
        Task<PagedResult<CommunicationAuditEntryDto>> GetAuditAsync(Guid notificationId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
    }

    // Template service
    public interface ITemplateService
    {
        Task<TemplateDto> CreateTemplateAsync(TemplateDto template, CancellationToken ct = default);
        Task<TemplateDto?> GetTemplateAsync(string templateId, CancellationToken ct = default);
        Task<PagedResult<TemplateDto>> ListTemplatesAsync(TemplateScope? scope = null, string? scopeKey = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
        Task<TemplateDto> UpdateTemplateAsync(TemplateDto template, CancellationToken ct = default);
        Task<bool> DeleteTemplateAsync(string templateId, CancellationToken ct = default);
        Task<(string Subject, string Body)> RenderAsync(TemplateDto template, IDictionary<string, object?>? variables, CancellationToken ct = default);
    }

    // Queue abstraction (durable queue implemented on SQL Server via EF)
    public interface ICommQueue
    {
        Task EnqueueAsync(NotificationDto notification, CancellationToken ct = default);
        Task<NotificationDto?> DequeueAsync(CancellationToken ct = default); // returns null if none
        Task AcknowledgeAsync(Guid notificationId, SendResultDto result, CancellationToken ct = default);
        Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default);
    }

    // Low-level channel senders
    public interface IEmailSender
    {
        Task<SendResultDto> SendEmailAsync(EmailDto email, CancellationToken ct = default);
    }

    public interface ISmsSender
    {
        Task<SendResultDto> SendSmsAsync(SmsDto sms, CancellationToken ct = default);
    }

    public interface IPushSender
    {
        Task<SendResultDto> SendPushAsync(PushDto push, CancellationToken ct = default);
    }

    public interface IWebhookSender
    {
        Task<SendResultDto> SendWebhookAsync(WebhookDto webhook, CancellationToken ct = default);
    }

    // Attachment service integrates S3 and DynamoDB and persists pointers in SQL Server via repositories
    public interface IAttachmentService
    {
        Task<AttachmentDescriptor> UploadAsync(Stream content, string filename, string contentType, Guid uploaderUserId, Guid? messageId = null, CancellationToken ct = default);
        Task<Stream?> DownloadAsync(Guid attachmentId, CancellationToken ct = default);
        Task<AttachmentDescriptor?> GetMetadataAsync(Guid attachmentId, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid attachmentId, CancellationToken ct = default);
        Task<string> GetPresignedUrlAsync(Guid attachmentId, TimeSpan expiry, CancellationToken ct = default);
    }

    // Audit store
    public interface ICommAuditStore
    {
        Task RecordAsync(CommunicationAuditEntryDto entry, CancellationToken ct = default);
        Task<PagedResult<CommunicationAuditEntryDto>> QueryAsync(Guid? notificationId = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
    }

    // Inbound webhook handler for receipts and replies
    public interface IInboundWebhookHandler
    {
        Task HandleDeliveryReceiptAsync(string provider, IDictionary<string, string> headers, byte[] payload, CancellationToken ct = default);
        Task HandleInboundReplyAsync(string provider, IDictionary<string, string> headers, byte[] payload, CancellationToken ct = default);
    }

    // Connection manager for realtime nodes
    //public interface IConnectionManager
    //{
    //    void AddConnection(Guid userId, string connectionId, string? nodeId = null);
    //    void RemoveConnection(Guid userId, string connectionId);
    //    IReadOnlyList<string> GetConnections(Guid userId);
    //    IReadOnlyList<Guid> GetOnlineUsers();
    //    Task BroadcastToUserAsync(Guid userId, WsEnvelope envelope);
    //    Task BroadcastToRoomAsync(Guid roomId, WsEnvelope envelope);
    //}

    #endregion

    #region Low-level DTOs for channel adapters

    public sealed class EmailDto
    {
        public string To { get; set; } = string.Empty;
        public string? ToName { get; set; }
        public string From { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
        public string BodyText { get; set; } = string.Empty;
        public IDictionary<string, string>? Headers { get; set; }
        public IDictionary<string, byte[]?>? Attachments { get; set; } // filename -> bytes
    }

    public sealed class SmsDto
    {
        public string ToPhone { get; set; } = string.Empty;
        public string FromPhone { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }

    public sealed class PushDto
    {
        public string DeviceToken { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public IDictionary<string, string>? Data { get; set; }
    }

    public sealed class WebhookDto
    {
        public string Url { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = "POST";
        public IDictionary<string, string>? Headers { get; set; }
        public byte[]? Payload { get; set; }
    }

    #endregion

    #region Repository interfaces (EF-backed implementations expected)

    public interface IMessageRepository
    {
        Task SaveMessageAsync(ChatMessageDto msg, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetRoomMessagesAsync(Guid roomId, int limit, string? continuationToken, CancellationToken ct = default);
        Task<PagedResult<ChatMessageDto>> GetUserInboxAsync(long userId, int limit, string? continuationToken, CancellationToken ct = default);
        Task MarkMessageReadAsync(Guid userId, Guid messageId, CancellationToken ct = default);
        Task AddAttachmentAsync(AttachmentDescriptor att, CancellationToken ct = default);
        Task<IReadOnlyList<AttachmentDescriptor>> GetAttachmentsForMessageAsync(Guid messageId, CancellationToken ct = default);
    }

    //public interface INotificationRepository
    //{
    //    Task EnqueueNotificationAsync(NotificationDto note, CancellationToken ct = default);
    //    Task<IReadOnlyList<NotificationDto>> DequeuePendingAsync(int max, CancellationToken ct = default);
    //    Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, CancellationToken ct = default);
    //    Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default);
    //}

    // src/ProjectsPlus.Comms/Persistence/INotificationRepository.cs  (or update existing INotificationRepository)
    public interface INotificationRepository
    {
        Task EnqueueNotificationAsync(NotificationDto note, CancellationToken ct = default);
        Task<IReadOnlyList<NotificationDto>> DequeuePendingAsync(int max, CancellationToken ct = default);
        Task UpdateNotificationAttemptAsync(Guid notificationId, int attempts, string? providerMessageId, string? status, DateTime? scheduledFor = null, CancellationToken ct = default);
        Task MoveToDeadLetterAsync(Guid notificationId, string reason, CancellationToken ct = default);
        Task RescheduleAsync(Guid notificationId, DateTime nextRun, CancellationToken ct = default);
    }

    #endregion
}
