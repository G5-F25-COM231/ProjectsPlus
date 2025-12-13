// src/ProjectsPlus.Comms/Presence/InMemoryPresenceService.cs
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.InMemory
{
    public class InMemoryPresenceService : IPresenceService
    {
        private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, string>> _store = new();

        public Task SetPresenceAsync(long userId, string connectionId, string status, string? metadataJson = null, CancellationToken ct = default)
        {
            var user = _store.GetOrAdd(userId, _ => new ConcurrentDictionary<string, string>());
            var payload = JsonSerializer.Serialize(new { connectionId, status, metadata = metadataJson, ts = DateTime.UtcNow });
            user[connectionId] = payload;
            return Task.CompletedTask;
        }

        public Task RemovePresenceAsync(long userId, string connectionId, CancellationToken ct = default)
        {
            if (_store.TryGetValue(userId, out var user))
            {
                user.TryRemove(connectionId, out _);
                if (user.IsEmpty) _store.TryRemove(userId, out _);
            }
            return Task.CompletedTask;
        }

        public Task<bool> IsOnlineAsync(long userId, CancellationToken ct = default)
        {
            var online = _store.TryGetValue(userId, out var user) && !user.IsEmpty;
            return Task.FromResult(online);
        }

        public Task<int> GetConnectionCountAsync(long userId, CancellationToken ct = default)
        {
            var count = _store.TryGetValue(userId, out var user) ? user.Count : 0;
            return Task.FromResult(count);
        }
    }
}
