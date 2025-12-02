// src/ProjectsPlus.Comms/Notifications/InAppNotificationProvider.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class InAppNotificationProvider : INotificationProvider
    {
        private readonly IConnectionManager _connections;
        private readonly ILogger<InAppNotificationProvider> _logger;

        public InAppNotificationProvider(IConnectionManager connections, ILogger<InAppNotificationProvider> logger)
        {
            _connections = connections;
            _logger = logger;
        }

        public bool CanHandle(string channel) => string.Equals(channel, "InApp", StringComparison.OrdinalIgnoreCase);

        public async Task<string> SendAsync(NotificationEntity notification, CancellationToken ct = default)
        {
            // send to user id (recipient expected to be user id string)
            if (!Guid.TryParse(notification.Recipient, out var userId))
            {
                // if recipient is numeric id or other format, adapt as needed
                _logger.LogWarning("InApp recipient not a GUID {Recipient}", notification.Recipient);
            }

            var env = new RealtimeEnvelope
            {
                Type = "notification.inapp",
                To = notification.Recipient,
                Payload = new Dictionary<string, object?>
                {
                    ["notificationId"] = notification.NotificationId,
                    ["subject"] = notification.Subject,
                    ["body"] = notification.Body
                }
            };

            await _connections.SendToUserAsync(userId, env, ct).ConfigureAwait(false);
            return $"inapp:{notification.NotificationId}";
        }
    }
}
