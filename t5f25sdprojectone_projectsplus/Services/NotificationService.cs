// src/ProjectsPlus.Comms/Notifications/EfNotificationService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(ProjectsPlusDbContext db, ILogger<NotificationService> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Guid> EnqueueAsync(NotificationCreateDto dto, CancellationToken ct = default)
        {
            var n = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                Channel = dto.Channel,
                Recipient = dto.Recipient,
                Subject = dto.Subject,
                Body = dto.Body,
                VariablesJson = dto.VariablesJson,
                Priority = dto.Priority,
                Status = "pending",
                Attempts = 0,
                ScheduledFor = dto.ScheduledForUtc,
                CreatedAt = DateTime.UtcNow
            };

            _db.Notifications.Add(n);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return n.NotificationId;
        }

        public async Task MarkAsSentAsync(Guid notificationId, string providerMessageId, CancellationToken ct = default)
        {
            var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == notificationId, ct).ConfigureAwait(false);
            if (n == null) return;
            n.Status = "sent";
            n.Attempts += 1;
            n.MetadataJson = (n.MetadataJson ?? "{}").Replace("}", $",\"providerMessageId\":\"{providerMessageId}\"}}");
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

            _db.CommAudit.Add(new CommAuditEntity
            {
                AuditId = Guid.NewGuid(),
                NotificationId = n.NotificationId,
                Channel = n.Channel,
                Recipient = n.Recipient,
                Status = "Sent",
                ProviderMessageId = providerMessageId,
                Timestamp = DateTime.UtcNow,
                PayloadJson = "{}"
            });
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task MarkAsFailedAsync(Guid notificationId, string error, CancellationToken ct = default)
        {
            var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == notificationId, ct).ConfigureAwait(false);
            if (n == null) return;

            n.Attempts += 1;
            n.LastAttemptAt = DateTime.UtcNow;

            const int maxAttempts = 5;
            if (n.Attempts >= maxAttempts)
            {
                // move to dead letters
                _db.DeadLetters.Add(new DeadLetterEntity
                {
                    DeadId = Guid.NewGuid(),
                    NotificationId = n.NotificationId,
                    Reason = error,
                    Attempts = n.Attempts,
                    LastError = error,
                    CreatedAt = DateTime.UtcNow,
                    PayloadJson = "{}"
                });
                n.Status = "dead";
            }
            else
            {
                // schedule retry with exponential backoff
                var backoffSeconds = (int)Math.Pow(2, n.Attempts) * 30;
                n.ScheduledFor = DateTime.UtcNow.AddSeconds(backoffSeconds);
                n.Status = "pending";
            }

            _db.CommAudit.Add(new CommAuditEntity
            {
                AuditId = Guid.NewGuid(),
                NotificationId = n.NotificationId,
                Channel = n.Channel,
                Recipient = n.Recipient,
                Status = "Failed",
                ProviderMessageId = null,
                ErrorMessage = error,
                Timestamp = DateTime.UtcNow,
                PayloadJson = "{}"
            });

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
