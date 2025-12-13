// src/ProjectsPlus.Comms/Presence/PresenceEventPersisterBackgroundService.cs
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class PresenceEventPersisterBackgroundService : BackgroundService
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly IServiceProvider _services;
        private readonly ILogger<PresenceEventPersisterBackgroundService> _logger;
        private const string PresenceChannel = "presence:events";

        public PresenceEventPersisterBackgroundService(IConnectionMultiplexer mux, IServiceProvider services, ILogger<PresenceEventPersisterBackgroundService> logger)
        {
            _mux = mux ?? throw new ArgumentNullException(nameof(mux));
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sub = _mux.GetSubscriber();
            return sub.SubscribeAsync(PresenceChannel, async (channel, message) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(utf8Json : message);
                    var root = doc.RootElement;
                    var type = root.GetProperty("type").GetString();
                    var userId = root.GetProperty("userId").GetInt64();
                    var connectionId = root.GetProperty("connectionId").GetString();
                    var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
                    var metadata = root.TryGetProperty("metadata", out var m) ? m.GetRawText() : null;
                    var ts = root.TryGetProperty("ts", out var t) ? t.GetDateTime() : DateTime.UtcNow;

                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();

                    var evt = new PresenceEventEntity
                    {
                        PresenceEventId = Guid.NewGuid(),
                        UserId = MessageRepository.ConvertLongToGuid(userId),
                        Status = status ?? (type == "leave" ? "offline" : "unknown"),
                        TimestampUtc = ts,
                        ConnectionId = connectionId,
                        MetadataJson = metadata
                    };

                    db.PresenceEvents.Add(evt);
                    await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist presence event");
                }
            }, CommandFlags.FireAndForget);
        }
    }
}
