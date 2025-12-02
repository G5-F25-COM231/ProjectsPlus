// src/ProjectsPlus.Comms/Workers/CommQueueWorker.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// Options for CommQueueWorker
    /// </summary>
    public sealed class CommQueueWorkerOptions
    {
        /// <summary>Maximum notifications to dequeue per loop</summary>
        public int MaxBatchSize { get; set; } = 10;

        /// <summary>Base delay between empty-poll loops</summary>
        public TimeSpan PollDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Maximum attempts before moving to dead-letter</summary>
        public int MaxAttempts { get; set; } = 5;

        /// <summary>Maximum backoff multiplier (exponential backoff)</summary>
        public int MaxBackoffMultiplier { get; set; } = 8;

        /// <summary>Whether to jitter backoff to avoid thundering herd</summary>
        public bool UseJitter { get; set; } = true;
    }

    /// <summary>
    /// Background worker that dequeues pending notifications and sends them.
    /// - Uses INotificationRepository as the durable queue (EF-backed)
    /// - Uses NotificationCenter to render/send
    /// - Writes audit entries via ICommAuditStore
    /// - Updates attempts and moves to dead-letter via repository methods
    /// </summary>
    public class CommQueueWorker : BackgroundService
    {
        private readonly ILogger<CommQueueWorker> _logger;
        private readonly INotificationRepository _notificationRepo;
        private readonly NotificationCenter _notificationCenter;
        private readonly ICommAuditStore _auditStore;
        private readonly CommQueueWorkerOptions _opts;
        private readonly Random _rng = new();

        public CommQueueWorker(
            ILogger<CommQueueWorker> logger,
            INotificationRepository notificationRepo,
            NotificationCenter notificationCenter,
            ICommAuditStore auditStore,
            IOptions<CommQueueWorkerOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationRepo = notificationRepo ?? throw new ArgumentNullException(nameof(notificationRepo));
            _notificationCenter = notificationCenter ?? throw new ArgumentNullException(nameof(notificationCenter));
            _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
            _opts = options?.Value ?? new CommQueueWorkerOptions();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CommQueueWorker starting (batch={Batch}, maxAttempts={MaxAttempts})", _opts.MaxBatchSize, _opts.MaxAttempts);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var items = await _notificationRepo.DequeuePendingAsync(_opts.MaxBatchSize, stoppingToken).ConfigureAwait(false);

                    if (items == null || items.Count == 0)
                    {
                        await Task.Delay(_opts.PollDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    var tasks = new List<Task>();
                    foreach (var note in items)
                    {
                        tasks.Add(ProcessNotificationAsync(note, stoppingToken));
                    }

                    // process in parallel but don't await with Task.WhenAll without observing exceptions
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // graceful shutdown
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error in CommQueueWorker loop; sleeping briefly before retry");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("CommQueueWorker stopping");
        }

        private async Task ProcessNotificationAsync(NotificationDto note, CancellationToken ct)
        {
            if (note == null) return;

            _logger.LogDebug("Processing notification {NotificationId} channel={Channel} recipient={Recipient}", note.NotificationId, note.Channel, note.Recipient);

            SendResultDto result;
            try
            {
                // SendNowAsync will render templates if present and call channel adapters
                result = await _notificationCenter.SendNowAsync(note, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SendNowAsync threw for notification {NotificationId}", note.NotificationId);

                result = new SendResultDto
                {
                    NotificationId = note.NotificationId,
                    Status = SendStatus.Failed,
                    ErrorMessage = ex.Message,
                    Timestamp = DateTime.UtcNow,
                    Attempt = 1
                };
            }

            // Record audit entry
            try
            {
                var audit = new CommunicationAuditEntryDto
                {
                    AuditId = Guid.NewGuid(),
                    NotificationId = note.NotificationId,
                    Channel = note.Channel,
                    Recipient = note.Recipient,
                    Status = result.Status,
                    ProviderMessageId = result.ProviderMessageId,
                    ErrorMessage = result.ErrorMessage,
                    Timestamp = result.Timestamp,
                    Metadata = note.Metadata,
                    PayloadJson = null
                };

                await _auditStore.RecordAsync(audit, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write audit for notification {NotificationId}", note.NotificationId);
            }

            // Update queue state (attempts/status) and handle retries / dead-letter
            try
            {
                var attempts = result.Attempt;
                var status = result.Status.ToString();

                await _notificationRepo.UpdateNotificationAttemptAsync(note.NotificationId, attempts, result.ProviderMessageId, status, DateTime.UtcNow.AddMinutes(30), ct).ConfigureAwait(false);

                if (result.Status == SendStatus.Failed)
                {
                    // If attempts >= max, move to dead-letter
                    if (attempts >= _opts.MaxAttempts)
                    {
                        var reason = result.ErrorMessage ?? "max attempts reached";
                        _logger.LogInformation("Moving notification {NotificationId} to dead-letter after {Attempts} attempts", note.NotificationId, attempts);
                        await _notificationRepo.MoveToDeadLetterAsync(note.NotificationId, reason, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        // compute backoff and reschedule by updating ScheduledFor on the notification entity
                        var backoff = ComputeBackoff(attempts);
                        var nextSchedule = DateTime.UtcNow.Add(backoff);

                        // We don't have a direct repo method to reschedule; UpdateNotificationAttemptAsync can be used to set attempts/status.
                        // If your repository supports setting ScheduledFor, extend UpdateNotificationAttemptAsync or add a dedicated method.
                        // For now we rely on the repository worker to pick up notifications where ScheduledFor <= now.
                        _logger.LogInformation("Notification {NotificationId} failed (attempt {Attempt}); next attempt in {Backoff}", note.NotificationId, attempts, backoff);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update queue state for notification {NotificationId}", note.NotificationId);
            }
        }

        private TimeSpan ComputeBackoff(int attempts)
        {
            // exponential backoff: base 2^attempts seconds, capped by MaxBackoffMultiplier
            var multiplier = Math.Min((int)Math.Pow(2, Math.Max(0, attempts - 1)), _opts.MaxBackoffMultiplier);
            var seconds = Math.Max(1, multiplier);
            if (_opts.UseJitter)
            {
                // jitter +/- 25%
                var jitter = 0.75 + _rng.NextDouble() * 0.5; // 0.75..1.25
                seconds = (int)Math.Max(1, Math.Round(seconds * jitter));
            }
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
