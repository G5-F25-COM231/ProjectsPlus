// src/ProjectsPlus.Comms/Presence/RedisPresenceService.cs
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class RedisPresenceService : IPresenceService, IDisposable
    {
        private readonly IConnectionMultiplexer _mux;
        private readonly IDatabase _db;
        private readonly ISubscriber _sub;
        private readonly ILogger<RedisPresenceService> _logger;
        private const string PresenceKeyPrefix = "presence:user:"; // presence:user:{userId}
        private const string PresenceChannel = "presence:events";

        public RedisPresenceService(IConnectionMultiplexer mux, ILogger<RedisPresenceService> logger)
        {
            _mux = mux ?? throw new ArgumentNullException(nameof(mux));
            _db = _mux.GetDatabase();
            _sub = _mux.GetSubscriber();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task SetPresenceAsync(long userId, string connectionId, string status, string? metadataJson = null, CancellationToken ct = default)
        {
            var key = PresenceKeyPrefix + userId;
            var payload = JsonSerializer.Serialize(new { connectionId, status, metadata = metadataJson, ts = DateTime.UtcNow });
            await _db.HashSetAsync(key, connectionId, payload).ConfigureAwait(false);
            await _db.KeyExpireAsync(key, TimeSpan.FromDays(1)).ConfigureAwait(false);

            var evt = JsonSerializer.Serialize(new { type = "join", userId, connectionId, status, metadata = metadataJson, ts = DateTime.UtcNow });
            await _sub.PublishAsync(PresenceChannel, evt).ConfigureAwait(false);
        }

        public async Task RemovePresenceAsync(long userId, string connectionId, CancellationToken ct = default)
        {
            var key = PresenceKeyPrefix + userId;
            await _db.HashDeleteAsync(key, connectionId).ConfigureAwait(false);
            var remaining = await _db.HashLengthAsync(key).ConfigureAwait(false);
            if (remaining == 0) await _db.KeyDeleteAsync(key).ConfigureAwait(false);

            var evt = JsonSerializer.Serialize(new { type = "leave", userId, connectionId, ts = DateTime.UtcNow });
            await _sub.PublishAsync(PresenceChannel, evt).ConfigureAwait(false);
        }

        public async Task<bool> IsOnlineAsync(long userId, CancellationToken ct = default)
        {
            var key = PresenceKeyPrefix + userId;
            var len = await _db.HashLengthAsync(key).ConfigureAwait(false);
            return len > 0;
        }

        public async Task<int> GetConnectionCountAsync(long userId, CancellationToken ct = default)
        {
            var key = PresenceKeyPrefix + userId;
            var len = await _db.HashLengthAsync(key).ConfigureAwait(false);
            return (int)len;
        }

        public void Dispose()
        {
            // Do not dispose shared IConnectionMultiplexer from DI here.
        }
    }
}
