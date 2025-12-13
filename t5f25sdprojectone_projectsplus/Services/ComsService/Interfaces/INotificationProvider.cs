// src/ProjectsPlus.Comms/Notifications/INotificationProvider.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public interface INotificationProvider
    {
        /// <summary>
        /// Sends the notification and returns a provider message id on success.
        /// Throws on failure.
        /// </summary>
        Task<string> SendAsync(NotificationEntity notification, CancellationToken ct = default);
        bool CanHandle(string channel);
    }
}
