// src/ProjectsPlus.Comms/Services/NotificationCenter.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// NotificationCenter orchestrates template rendering, channel mapping and queueing/sending.
    /// - EnqueueNotificationAsync: validates and enqueues to durable queue (ICommQueue)
    /// - SendNowAsync: renders (if template provided) and sends immediately via channel adapters
    /// - GetAuditAsync: queries audit store (ICommAuditStore)
    /// </summary>
    public class NotificationCenter : INotificationCenter
    {
        private readonly ITemplateService _templates;
        private readonly ICommQueue _queue;
        private readonly IEmailSender _emailSender;
        private readonly IWebhookSender _webhookSender;
        private readonly ISmsSender? _smsSender;
        private readonly IPushSender? _pushSender;
        private readonly ICommAuditStore _auditStore;

        public NotificationCenter(
            ITemplateService templates,
            ICommQueue queue,
            IEmailSender emailSender,
            IWebhookSender webhookSender,
            ICommAuditStore auditStore,
            ISmsSender? smsSender = null,
            IPushSender? pushSender = null)
        {
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
            _webhookSender = webhookSender ?? throw new ArgumentNullException(nameof(webhookSender));
            _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
            _smsSender = smsSender;
            _pushSender = pushSender;
        }

        /// <summary>
        /// Enqueue a notification for later delivery by the CommQueue worker.
        /// Returns a SendResultDto with Status Pending.
        /// </summary>
        public async Task<SendResultDto> EnqueueNotificationAsync(NotificationDto notification, CancellationToken ct = default)
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (string.IsNullOrWhiteSpace(notification.Recipient)) throw new ArgumentException("Recipient is required", nameof(notification));

            // Normalize minimal fields
            notification.CreatedAt = notification.CreatedAt == default ? DateTime.UtcNow : notification.CreatedAt;
            if (notification.NotificationId == Guid.Empty) notification.NotificationId = Guid.NewGuid();

            // If template reference provided, ensure template exists (best-effort)
            if (notification.Template != null)
            {
                var tpl = await _templates.GetTemplateAsync(notification.Template.TemplateId, ct);
                if (tpl == null)
                {
                    // record audit entry for missing template and return failed result
                    var failed = new SendResultDto
                    {
                        NotificationId = notification.NotificationId,
                        Status = SendStatus.Failed,
                        ErrorMessage = $"Template '{notification.Template.TemplateId}' not found",
                        Timestamp = DateTime.UtcNow,
                        Attempt = 0
                    };

                    await _auditStore.RecordAsync(new CommunicationAuditEntryDto
                    {
                        AuditId = Guid.NewGuid(),
                        NotificationId = notification.NotificationId,
                        Channel = notification.Channel,
                        Recipient = notification.Recipient,
                        Status = SendStatus.Failed,
                        ErrorMessage = failed.ErrorMessage,
                        Timestamp = failed.Timestamp
                    }, ct);

                    return failed;
                }
            }

            // Enqueue to durable queue
            await _queue.EnqueueAsync(notification, ct);

            var result = new SendResultDto
            {
                NotificationId = notification.NotificationId,
                Status = SendStatus.Pending,
                Timestamp = DateTime.UtcNow,
                Attempt = 0
            };

            // record audit (queued)
            await _auditStore.RecordAsync(new CommunicationAuditEntryDto
            {
                AuditId = Guid.NewGuid(),
                NotificationId = notification.NotificationId,
                Channel = notification.Channel,
                Recipient = notification.Recipient,
                Status = SendStatus.Pending,
                Timestamp = result.Timestamp
            }, ct);

            return result;
        }

        /// <summary>
        /// Send a notification immediately (synchronously) using the appropriate channel adapter.
        /// This method will render templates if provided, call the channel sender, record audit,
        /// and return the SendResultDto from the adapter (or synthesized result on error).
        /// </summary>
        public async Task<SendResultDto> SendNowAsync(NotificationDto notification, CancellationToken ct = default)
        {
            if (notification == null) throw new ArgumentNullException(nameof(notification));
            if (string.IsNullOrWhiteSpace(notification.Recipient)) throw new ArgumentException("Recipient is required", nameof(notification));

            notification.CreatedAt = notification.CreatedAt == default ? DateTime.UtcNow : notification.CreatedAt;
            if (notification.NotificationId == Guid.Empty) notification.NotificationId = Guid.NewGuid();

            // If template provided, render it
            if (notification.Template != null)
            {
                var tpl = await _templates.GetTemplateAsync(notification.Template.TemplateId, ct);
                if (tpl != null)
                {
                    var (subject, body) = await _templates.RenderAsync(tpl, notification.Variables, ct);
                    // prefer explicit subject/body if provided on notification; otherwise use rendered
                    notification.Subject ??= subject;
                    notification.Body ??= body;
                }
            }

            SendResultDto sendResult;

            try
            {
                switch (notification.Channel)
                {
                    case ChannelType.Email:
                        var email = new EmailDto
                        {
                            To = notification.Recipient,
                            ToName = notification.RecipientDisplayName,
                            From = "no-reply@projectsplus.local",
                            Subject = notification.Subject ?? string.Empty,
                            BodyHtml = notification.Body ?? string.Empty,
                            BodyText = StripHtml(notification.Body ?? string.Empty)
                        };
                        sendResult = await _emailSender.SendEmailAsync(email, ct);
                        break;

                    case ChannelType.Webhook:
                        var webhook = new WebhookDto
                        {
                            Url = notification.Recipient,
                            HttpMethod = "POST",
                            Headers = new Dictionary<string, string> { { "Content-Type", "application/json" } },
                            Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
                            {
                                subject = notification.Subject,
                                body = notification.Body,
                                variables = notification.Variables,
                                metadata = notification.Metadata
                            })
                        };
                        sendResult = await _webhookSender.SendWebhookAsync(webhook, ct);
                        break;

                    case ChannelType.Sms:
                        if (_smsSender == null)
                            throw new InvalidOperationException("SMS sender not configured");
                        var sms = new SmsDto
                        {
                            ToPhone = notification.Recipient,
                            FromPhone = string.Empty,
                            Body = notification.Body ?? notification.Subject ?? string.Empty
                        };
                        sendResult = await _smsSender.SendSmsAsync(sms, ct);
                        break;

                    case ChannelType.Push:
                        if (_pushSender == null)
                            throw new InvalidOperationException("Push sender not configured");
                        var push = new PushDto
                        {
                            DeviceToken = notification.Recipient,
                            Title = notification.Subject ?? string.Empty,
                            Body = notification.Body ?? string.Empty
                        };
                        sendResult = await _pushSender.SendPushAsync(push, ct);
                        break;

                    case ChannelType.InApp:
                        // InApp: treat as delivered immediately (the app will surface it)
                        sendResult = new SendResultDto
                        {
                            NotificationId = notification.NotificationId,
                            Status = SendStatus.Sent,
                            ProviderMessageId = null,
                            ErrorMessage = null,
                            Timestamp = DateTime.UtcNow,
                            Attempt = 1
                        };
                        break;

                    default:
                        throw new NotSupportedException($"Channel {notification.Channel} not supported");
                }
            }
            catch (Exception ex)
            {
                sendResult = new SendResultDto
                {
                    NotificationId = notification.NotificationId,
                    Status = SendStatus.Failed,
                    ProviderMessageId = null,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }

            // Record audit entry
            await _auditStore.RecordAsync(new CommunicationAuditEntryDto
            {
                AuditId = Guid.NewGuid(),
                NotificationId = notification.NotificationId,
                Channel = notification.Channel,
                Recipient = notification.Recipient,
                Status = sendResult.Status,
                ProviderMessageId = sendResult.ProviderMessageId,
                ErrorMessage = sendResult.ErrorMessage,
                Timestamp = sendResult.Timestamp,
                PayloadJson = null
            }, ct);

            // If we have a durable queue, acknowledge/update queue state
            try
            {
                await _queue.AcknowledgeAsync(notification.NotificationId, sendResult, ct);
            }
            catch
            {
                // best-effort: ignore queue failures here (worker will reconcile)
            }

            return sendResult;
        }

        /// <summary>
        /// Query audit entries for a notification.
        /// </summary>
        public async Task<PagedResult<CommunicationAuditEntryDto>> GetAuditAsync(Guid notificationId, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        {
            // The ICommAuditStore.QueryAsync returns a PagedResult<CommunicationAuditEntryDto>
            return await _auditStore.QueryAsync(notificationId, pageSize, continuationToken, ct);
        }

        #region Helpers

        private static string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            // very small, permissive stripper for plain text fallback
            var arr = new char[html.Length];
            var idx = 0;
            var inside = false;
            foreach (var ch in html)
            {
                if (ch == '<') { inside = true; continue; }
                if (ch == '>') { inside = false; continue; }
                if (!inside) arr[idx++] = ch;
            }
            return new string(arr, 0, idx).Trim();
        }

        #endregion
    }


    public interface IChannelAdapterFactory
    {
        IChannelAdapter GetAdapter(ChannelType channel);
    }

}
