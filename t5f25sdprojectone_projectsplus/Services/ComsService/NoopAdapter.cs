// src/ProjectsPlus.Comms/Channels/NoopAdapter.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public class NoopAdapter : IChannelAdapter
    {
        private readonly ILogger<NoopAdapter> _logger;

        public NoopAdapter(ILogger<NoopAdapter> logger) => _logger = logger;

        public Task<SendResultDto> SendAsync(NotificationSendRequest request, CancellationToken ct = default)
        {
            _logger.LogDebug("NoopAdapter invoked for {NotificationId} recipient={Recipient}", request.NotificationId, request.Recipient);
            var res = new SendResultDto
            {
                NotificationId = request.NotificationId,
                Status = SendStatus.Sent,
                ProviderMessageId = $"noop-{Guid.NewGuid():D}",
                Timestamp = DateTime.UtcNow,
                Attempt = request.Attempt
            };
            return Task.FromResult(res);
        }
    }
}
