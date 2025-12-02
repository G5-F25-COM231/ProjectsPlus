// src/ProjectsPlus.Comms/Channels/WebhookAdapter.cs
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    public sealed class WebhookAdapterOptions
    {
        public int MaxRetries { get; init; } = 4;
        public int BaseBackoffSeconds { get; init; } = 2;
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    }

    public class WebhookAdapter : IChannelAdapter
    {
        private readonly HttpClient _http;
        private readonly WebhookAdapterOptions _opts;
        private readonly ILogger<WebhookAdapter> _logger;

        public WebhookAdapter(HttpClient http, IOptions<WebhookAdapterOptions> opts, ILogger<WebhookAdapter> logger)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _opts = opts?.Value ?? throw new ArgumentNullException(nameof(opts));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SendResultDto> SendAsync(NotificationSendRequest request, CancellationToken ct = default)
        {
            var attempt = Math.Max(1, request.Attempt);
            var result = new SendResultDto { NotificationId = request.NotificationId, Attempt = attempt };

            // payload shape can be adjusted to your webhook contract
            var payload = new
            {
                id = request.NotificationId,
                recipient = request.Recipient,
                subject = request.Subject,
                body = request.Body,
                variables = request.Variables,
                metadata = request.Metadata
            };

            // Expect recipient to be a URL for webhook adapter usage
            if (!Uri.TryCreate(request.Recipient, UriKind.Absolute, out var uri))
            {
                result.Status = SendStatus.Failed;
                result.ErrorMessage = "Invalid webhook URL";
                return result;
            }

            for (int i = 0; i < _opts.MaxRetries; i++)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(_opts.Timeout);

                    var resp = await _http.PostAsJsonAsync(uri, payload, cts.Token).ConfigureAwait(false);

                    if (resp.IsSuccessStatusCode)
                    {
                        result.Status = SendStatus.Sent;
                        result.ProviderMessageId = resp.Headers?.Location?.ToString() ?? resp.Headers?.ETag?.ToString() ?? Guid.NewGuid().ToString("D");
                        result.Timestamp = DateTime.UtcNow;
                        return result;
                    }

                    // treat 4xx as permanent failure
                    if ((int)resp.StatusCode >= 400 && (int)resp.StatusCode < 500)
                    {
                        result.Status = SendStatus.Failed;
                        result.ErrorMessage = $"Webhook returned {(int)resp.StatusCode}";
                        return result;
                    }

                    // 5xx transient
                    result.ErrorMessage = $"Webhook returned {(int)resp.StatusCode}";
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Webhook send attempt {Attempt} failed for {Url}", attempt + i, request.Recipient);
                    result.ErrorMessage = ex.Message;
                }

                // backoff
                var backoff = TimeSpan.FromSeconds(_opts.BaseBackoffSeconds * Math.Pow(2, i));
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }

            result.Status = SendStatus.Failed;
            return result;
        }
    }
}
