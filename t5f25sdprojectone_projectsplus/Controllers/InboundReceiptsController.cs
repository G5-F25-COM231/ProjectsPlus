// src/ProjectsPlus.Comms/Controllers/InboundReceiptsController.cs
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("api/inbound/receipts")]
    public class InboundReceiptsController : ControllerBase
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<InboundReceiptsController> _logger;

        public InboundReceiptsController(ProjectsPlusDbContext db, ILogger<InboundReceiptsController> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Receive a delivery/receipt callback from a channel (webhook, SMTP bounce, provider webhook).
        /// Persists a CommAudit row and updates Notification state when NotificationId is provided.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> PostReceipt([FromBody] ReceiptDto dto)
        {
            if (dto == null) return BadRequest("receipt payload required");

            try
            {
                // Persist audit row
                var audit = new CommAuditEntity
                {
                    AuditId = Guid.NewGuid(),
                    NotificationId = dto.NotificationId,
                    Channel = dto.Channel ?? "unknown",
                    Recipient = dto.Recipient,
                    EventType = dto.EventType ?? "receipt",
                    Timestamp = dto.TimestampUtc ?? DateTime.UtcNow,
                    PayloadJson = dto.Payload != null ? JsonSerializer.Serialize(dto.Payload) : null,
                    CorrelationId = dto.CorrelationId
                };

                _db.CommAudit.Add(audit);

                // If NotificationId provided, attempt to update notification status
                if (dto.NotificationId.HasValue)
                {
                    var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == dto.NotificationId.Value).ConfigureAwait(false);
                    if (notif != null)
                    {
                        // Map common event types to notification state changes
                        switch ((dto.EventType ?? string.Empty).ToLowerInvariant())
                        {
                            case "delivered":
                            case "delivery":
                                notif.Status = "Delivered";
                                notif.SentAt = dto.TimestampUtc ?? DateTime.UtcNow;
                                break;
                            case "failed":
                            case "bounce":
                            case "undeliverable":
                                notif.Status = "Failed";
                                notif.LastError = dto.ErrorMessage;
                                notif.Attempts = notif.Attempts <= 0 ? 1 : notif.Attempts;
                                break;
                            case "opened":
                                // optional: track opens separately if supported
                                notif.Metadata ??= new Dictionary<string, string>();
                                notif.Metadata["last_opened_at"] = (dto.TimestampUtc ?? DateTime.UtcNow).ToString("o");
                                break;
                            default:
                                // leave as-is for unknown event types
                                break;
                        }

                        _db.Notifications.Update(notif);
                    }
                }

                await _db.SaveChangesAsync().ConfigureAwait(false);
                return Accepted(new { auditId = audit.AuditId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist inbound receipt");
                return StatusCode(500, "failed to process receipt");
            }
        }
    }

    /// <summary>
    /// Minimal DTO for inbound receipts. Extend as needed.
    /// </summary>
    public class ReceiptDto
    {
        public Guid? NotificationId { get; set; } // optional link to Notification
        public string? Channel { get; set; } // e.g., email, webhook, sms
        public string? Recipient { get; set; } // email address, phone, device token
        public string? EventType { get; set; } // delivered, failed, opened, bounce, etc.
        public DateTime? TimestampUtc { get; set; }
        public object? Payload { get; set; } // raw provider payload
        public string? ErrorMessage { get; set; } // optional error/bounce reason
        public string? CorrelationId { get; set; } // optional correlation to job/project
    }
}
