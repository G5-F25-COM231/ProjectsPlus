// src/ProjectsPlus.Comms/Presence/IPresenceService.cs
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface IPresenceService
    {
        Task SetPresenceAsync(long userId, string connectionId, string status, string? metadataJson = null, CancellationToken ct = default);
        Task RemovePresenceAsync(long userId, string connectionId, CancellationToken ct = default);
        Task<bool> IsOnlineAsync(long userId, CancellationToken ct = default);
        Task<int> GetConnectionCountAsync(long userId, CancellationToken ct = default);
    }
}
