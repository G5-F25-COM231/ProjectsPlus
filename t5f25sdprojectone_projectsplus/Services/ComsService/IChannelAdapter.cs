// src/ProjectsPlus.Comms/Channels/IChannelAdapter.cs
using System;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public interface IChannelAdapter
    {
        /// <summary>
        /// Send a prepared notification payload. Implementations should return a SendResultDto
        /// describing provider message id, status, error message and attempt count.
        /// </summary>
        Task<SendResultDto> SendAsync(NotificationSendRequest request, CancellationToken ct = default);
    }

    public sealed class NotificationSendRequest
    {
        public Guid NotificationId { get; init; }
        public string Recipient { get; init; } = string.Empty;
        public string? RecipientDisplayName { get; init; }
        public string? Subject { get; init; }
        public string? Body { get; init; }
        public string? BodyHtml { get; init; }
        public IDictionary<string, object?>? Variables { get; init; }
        public IDictionary<string, string>? Metadata { get; init; }
        public int Attempt { get; init; } = 1;
    }

   
}
