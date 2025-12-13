// src/ProjectsPlus.Comms/Workers/CommQueueWorker.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class CommQueueWorkerOptions
    {
        public int MaxBatchSize { get; set; } = 10;
        public int MaxAttempts { get; set; } = 5;
        public TimeSpan PollDelay { get; set; } = TimeSpan.FromSeconds(1);
        public bool UseJitter { get; set; } = true;
    }

    /// <summary>
    /// Background worker that dequeues notifications and dispatches them via INotificationCenter.
    /// Constructor uses explicit typed dependencies to avoid argument-order confusion in tests.
    /// </summary>
    public class CommQueueWorker : IHostedService, IDisposable
    {
        private readonly ILogger<CommQueueWorker> _logger;
        private readonly INotificationRepository _notificationRepo;
        private readonly INotificationCenter _notificationCenter;
        private readonly ICommAuditStore _auditStore;
        private readonly CommQueueWorkerOptions _opts;
        private CancellationTokenSource? _cts;
        private Task? _executingTask;

        private readonly IServiceProvider _services;
        public CommQueueWorker(
            IServiceProvider services//,
            //ILogger<CommQueueWorker> logger,
            //INotificationRepository notificationRepo,
            //INotificationCenter notificationCenter,
            //ICommAuditStore auditStore,
            //IOptions<CommQueueWorkerOptions> options
        )
        {
             _services = services;
            using var scope = _services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var ntc = scope.ServiceProvider.GetRequiredService<INotificationCenter>();
            var cas = scope.ServiceProvider.GetRequiredService<ICommAuditStore>();
            var opts = scope.ServiceProvider.GetRequiredService<IOptions<CommQueueWorkerOptions>>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CommQueueWorker>>();


            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationRepo = repo ?? throw new ArgumentNullException(nameof(repo));
            _notificationCenter = ntc ?? throw new ArgumentNullException(nameof(ntc));
            _auditStore = cas ?? throw new ArgumentNullException(nameof(cas));
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executingTask = Task.Run(() => ExecuteAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts == null) return;
            _cts.Cancel();
            try
            {
                if (_executingTask != null)
                    await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        private async Task ExecuteAsync(CancellationToken ct)
        {
            _logger.LogInformation("CommQueueWorker started (batch={Batch}, attempts={Attempts})", _opts.MaxBatchSize, _opts.MaxAttempts);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var batch = await _notificationRepo.DequeuePendingAsync(_opts.MaxBatchSize, ct).ConfigureAwait(false);
                    if (batch == null || batch.Count == 0)
                    {
                        await Task.Delay(_opts.PollDelay, ct).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var note in batch)
                    {
                        if (ct.IsCancellationRequested) break;

                        SendResultDto result;
                        try
                        {
                            result = await _notificationCenter.SendNowAsync(note, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "SendNowAsync threw for notification {NotificationId}", note.NotificationId);
                            result = new SendResultDto
                            {
                                NotificationId = note.NotificationId,
                                Status = SendStatus.Failed,
                                ErrorMessage = ex.Message,
                                Timestamp = DateTime.UtcNow,
                                Attempt = 1
                            };
                        }

                        // Update attempts / move to dead letter if necessary
                        if (result.Status == SendStatus.Failed)
                        {
                            // increment attempts in repo (best-effort)
                            await _notificationRepo.UpdateNotificationAttemptAsync(note.NotificationId, result.Attempt, result.ProviderMessageId, result.Status.ToString(), null, ct).ConfigureAwait(false);

                            if (result.Attempt >= _opts.MaxAttempts)
                            {
                                await _notificationRepo.MoveToDeadLetterAsync(note.NotificationId, result.ErrorMessage ?? "max attempts reached", ct).ConfigureAwait(false);

                                // record audit
                                await _auditStore.RecordAsync(new CommunicationAuditEntryDto
                                {
                                    AuditId = Guid.NewGuid(),
                                    NotificationId = note.NotificationId,
                                    Channel = note.Channel,
                                    Recipient = note.Recipient,
                                    Status = SendStatus.Failed,
                                    ErrorMessage = result.ErrorMessage,
                                    Timestamp = DateTime.UtcNow
                                }, ct).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            // success path: acknowledge/update repo
                            await _notificationRepo.UpdateNotificationAttemptAsync(note.NotificationId, result.Attempt, result.ProviderMessageId, result.Status.ToString(), null, ct).ConfigureAwait(false);

                            await _auditStore.RecordAsync(new CommunicationAuditEntryDto
                            {
                                AuditId = Guid.NewGuid(),
                                NotificationId = note.NotificationId,
                                Channel = note.Channel,
                                Recipient = note.Recipient,
                                Status = result.Status,
                                ProviderMessageId = result.ProviderMessageId,
                                ErrorMessage = result.ErrorMessage,
                                Timestamp = result.Timestamp
                            }, ct).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // shutting down
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CommQueueWorker loop");
                    await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("CommQueueWorker stopping");
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
