// src/Infrastructure/Forwarding/RemoteForwarder.cs
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.RedisPatches.DdbAdapter
{
    /// <summary>
    /// Minimal HTTP-based remote forwarder.
    /// Sends a POST to the remote instance address with a small JSON envelope.
    /// The remote instance must expose an endpoint to accept forwarded messages.
    /// This class is intentionally small and dependency-injectable (HttpClient should be created via IHttpClientFactory).
    /// </summary>
    public sealed class RemoteForwarder : IRemoteForwarder
    {
        private readonly HttpClient _http;
        private readonly ILogger<RemoteForwarder> _logger;
        private readonly string _path; // relative path on remote instance to forward to

        public RemoteForwarder(HttpClient httpClient, ILogger<RemoteForwarder> logger, string forwardPath = "/internal/forward/message")
        {
            _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _path = string.IsNullOrWhiteSpace(forwardPath) ? "/internal/forward/message" : forwardPath;
        }

        /// <summary>
        /// Forward a message to a remote instance address.
        /// instanceAddress is expected to be a base URL (e.g., "https://host:port" or "http://host:port").
        /// </summary>
        public async Task ForwardMessageAsync(string instanceAddress, string messageId, string targetType, string targetId, string payload, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceAddress)) throw new ArgumentNullException(nameof(instanceAddress));
            if (string.IsNullOrWhiteSpace(messageId)) throw new ArgumentNullException(nameof(messageId));

            var baseUri = instanceAddress.TrimEnd('/');
            var uri = new Uri(baseUri + (_path.StartsWith("/") ? _path : "/" + _path), UriKind.Absolute);

            var envelope = new ForwardEnvelope
            {
                MessageId = messageId,
                TargetType = targetType,
                TargetId = targetId,
                Payload = payload
            };

            try
            {
                // Use PostAsJsonAsync for simplicity; caller's HttpClient can be configured with timeouts, auth, etc.
                var resp = await _http.PostAsJsonAsync(uri, envelope, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    var body = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogWarning("Forward to {Uri} returned {StatusCode}. Body: {Body}", uri, resp.StatusCode, body);
                    resp.EnsureSuccessStatusCode();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Forward to {Uri} cancelled for message {MessageId}", uri, messageId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to forward message {MessageId} to {Uri}", messageId, uri);
                throw;
            }
        }

        private static async Task<string?> SafeReadStringAsync(HttpResponseMessage resp)
        {
            try
            {
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        private sealed class ForwardEnvelope
        {
            public string MessageId { get; set; } = string.Empty;
            public string? TargetType { get; set; }
            public string? TargetId { get; set; }
            public string? Payload { get; set; }
        }
    }

    /// <summary>
    /// Remote forwarder contract used by DeliveryWorker.
    /// Kept here for convenience; DeliveryWorker already referenced this interface.
    /// </summary>
    public interface IRemoteForwarder
    {
        Task ForwardMessageAsync(string instanceAddress, string messageId, string targetType, string targetId, string payload, CancellationToken ct = default);
    }
}
