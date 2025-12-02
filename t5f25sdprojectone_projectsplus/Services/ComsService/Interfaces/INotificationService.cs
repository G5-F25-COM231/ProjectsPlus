// src/ProjectsPlus.Comms/Notifications/INotificationService.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface INotificationService
    {
        Task<Guid> EnqueueAsync(NotificationCreateDto dto, CancellationToken ct = default);
        Task MarkAsSentAsync(Guid notificationId, string providerMessageId, CancellationToken ct = default);
        Task MarkAsFailedAsync(Guid notificationId, string error, CancellationToken ct = default);
    }
}
