// src/ProjectsPlus.Comms/Notifications/NotificationDispatcherBackgroundService.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class NotificationDispatcherBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<NotificationDispatcherBackgroundService> _logger;
        private readonly INotificationProvider[] _providers;
        private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);
        private readonly int _batchSize = 20;

        public NotificationDispatcherBackgroundService(IServiceProvider services, ILogger<NotificationDispatcherBackgroundService> logger, INotificationProvider[] providers)
        {
            _services = services;
            _logger = logger;
            _providers = providers;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Notification dispatcher started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();

                    // claim a batch of pending notifications
                    var now = DateTime.UtcNow;
                    var pending = await db.Notifications
                        .Where(n => n.Status == "pending" && (n.ScheduledFor == null || n.ScheduledFor <= now))
                        .OrderBy(n => n.Priority).ThenBy(n => n.CreatedAt)
                        .Take(_batchSize)
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false);

                    foreach (var n in pending)
                    {
                        // mark processing to reduce duplicate work
                        n.Status = "processing";
                    }
                    await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                    foreach (var n in pending)
                    {
                        try
                        {
                            var provider = _providers.FirstOrDefault(p => p.CanHandle(n.Channel));
                            if (provider == null) throw new InvalidOperationException($"No provider for channel {n.Channel}");

                            var providerMessageId = await provider.SendAsync(n, stoppingToken).ConfigureAwait(false);

                            // mark sent
                            var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                            await svc.MarkAsSentAsync(n.NotificationId, providerMessageId, stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Notification {Id} failed", n.NotificationId);
                            var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
                            await svc.MarkAsFailedAsync(n.NotificationId, ex.Message, stoppingToken).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Dispatcher loop error");
                }

                await Task.Delay(_pollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
