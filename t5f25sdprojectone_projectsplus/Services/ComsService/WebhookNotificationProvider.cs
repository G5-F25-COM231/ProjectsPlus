// src/ProjectsPlus.Comms/Notifications/WebhookNotificationProvider.cs
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class WebhookNotificationProvider : INotificationProvider
    {
        private readonly HttpClient _http;
        private readonly ILogger<WebhookNotificationProvider> _logger;

        public WebhookNotificationProvider(HttpClient http, ILogger<WebhookNotificationProvider> logger)
        {
            _http = http;
            _logger = logger;
        }

        public bool CanHandle(string channel) => string.Equals(channel, "Webhook", StringComparison.OrdinalIgnoreCase);

        public async Task<string> SendAsync(NotificationEntity notification, CancellationToken ct = default)
        {
            var payload = new
            {
                notification.NotificationId,
                notification.Channel,
                notification.Recipient,
                notification.Subject,
                notification.Body,
                notification.VariablesJson
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(notification.Recipient, content, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook provider failed {Status} {Body}", resp.StatusCode, body);
                throw new InvalidOperationException($"Webhook returned {resp.StatusCode}");
            }

            // return provider message id as HTTP status + timestamp
            return $"webhook:{(int)resp.StatusCode}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        }
    }
}
