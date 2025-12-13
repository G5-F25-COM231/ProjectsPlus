// src/ProjectsPlus.Comms/Channels/WebhookSender.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// HTTP webhook sender. Uses IHttpClientFactory to obtain HttpClient instances,
    /// supports configurable timeouts, retry/backoff, custom headers and binary payloads.
    /// Bind options from configuration section "Comms:Webhook".
    /// </summary>
    public class WebhookSender : IWebhookSender
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly WebhookOptions _opts;
        private readonly ILogger<WebhookSender> _logger;

        public WebhookSender(IHttpClientFactory httpFactory, IOptions<WebhookOptions> opts, ILogger<WebhookSender> logger)
        {
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _opts = opts?.Value ?? new WebhookOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<SendResultDto> SendWebhookAsync(WebhookDto webhook, CancellationToken ct = default)
        {
            if (webhook == null) throw new ArgumentNullException(nameof(webhook));
            if (string.IsNullOrWhiteSpace(webhook.Url)) throw new ArgumentException("Webhook.Url is required", nameof(webhook));

            var result = new SendResultDto
            {
                NotificationId = Guid.Empty,
                Status = SendStatus.Failed,
                Timestamp = DateTime.UtcNow,
                Attempt = 0
            };

            // Determine HTTP method
            var method = HttpMethod.Post;
            if (!string.IsNullOrWhiteSpace(webhook.HttpMethod))
            {
                try { method = new HttpMethod(webhook.HttpMethod.ToUpperInvariant()); } catch { method = HttpMethod.Post; }
            }

            // Preserve original payload bytes so we can recreate HttpContent for retries
            var originalPayloadBytes = webhook.Payload;

            // Create HttpClient (named or default)
            var client = _httpFactory.CreateClient(_opts.HttpClientName ?? string.Empty);

            // Merge headers: options default headers then webhook headers (webhook headers override)
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_opts.DefaultHeaders != null)
            {
                foreach (var kv in _opts.DefaultHeaders) headers[kv.Key] = kv.Value;
            }
            if (webhook.Headers != null)
            {
                foreach (var kv in webhook.Headers) headers[kv.Key] = kv.Value;
            }

            // Configure attempts and backoff
            var attempts = Math.Max(1, _opts.MaxAttempts);
            var backoffBaseMs = Math.Max(100, _opts.BackoffBaseMs);

            // Per-request timeout token source (linked to caller token)
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                result.Attempt = attempt;

                // Recreate content for each attempt (HttpContent is not reusable)
                HttpContent? content = null;
                if (originalPayloadBytes != null && originalPayloadBytes.Length > 0)
                {
                    content = new ByteArrayContent(originalPayloadBytes);
                    if (!headers.ContainsKey("Content-Type"))
                        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                }

                // Build cancellation token source for per-request timeout if configured
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                if (_opts.RequestTimeout.HasValue)
                    cts.CancelAfter(_opts.RequestTimeout.Value);

                try
                {
                    using var req = new HttpRequestMessage(method, webhook.Url);

                    if (content != null && (method == HttpMethod.Post || method == HttpMethod.Put || method == HttpMethod.Patch || method.Method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)))
                    {
                        req.Content = content;
                    }

                    // Apply headers (content vs request)
                    foreach (var kv in headers)
                    {
                        if (req.Content != null && IsContentHeader(kv.Key))
                        {
                            if (!req.Content.Headers.Contains(kv.Key))
                                req.Content.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                        else
                        {
                            if (!req.Headers.Contains(kv.Key))
                                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                        }
                    }

                    // Send
                    var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);

                    result.Timestamp = DateTime.UtcNow;
                    result.Attempt = attempt;
                    result.ProviderMessageId = ExtractProviderMessageId(resp);
                    result.ErrorMessage = null;

                    if (resp.IsSuccessStatusCode)
                    {
                        result.Status = SendStatus.Sent;
                        return result;
                    }

                    // Non-success status code: read body for diagnostics
                    string body = string.Empty;
                    try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); } catch { }

                    result.Status = SendStatus.Failed;
                    result.ErrorMessage = $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}; Body: {Truncate(body, 1024)}";

                    // Decide whether to retry on transient codes
                    if (IsTransientStatus(resp.StatusCode) && attempt < attempts)
                    {
                        var delay = ComputeBackoff(attempt, backoffBaseMs);
                        _logger.LogWarning("Webhook transient failure {Status} to {Url}. Attempt {Attempt}/{Attempts}. Retrying in {Delay}ms. Error: {Error}",
                            resp.StatusCode, webhook.Url, attempt, attempts, delay, result.ErrorMessage);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    return result;
                }
                catch (OperationCanceledException oce) when (!ct.IsCancellationRequested)
                {
                    // timeout from internal cts
                    result.Status = SendStatus.Failed;
                    result.ErrorMessage = "Request timed out";
                    _logger.LogInformation(oce, "Webhook request timed out to {Url}", webhook.Url);
                    if (attempt < attempts)
                    {
                        var delay = ComputeBackoff(attempt, backoffBaseMs);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = SendStatus.Failed;
                    result.ErrorMessage = ex.Message;
                    _logger.LogError(ex, "Webhook send failed to {Url} on attempt {Attempt}/{Attempts}", webhook.Url, attempt, attempts);

                    if (attempt < attempts)
                    {
                        var delay = ComputeBackoff(attempt, backoffBaseMs);
                        await Task.Delay(delay, ct).ConfigureAwait(false);
                        continue;
                    }

                    return result;
                }
            }

            return result;
        }

        #region Helpers

        private static bool IsContentHeader(string headerName)
        {
            var contentHeaders = new[]
            {
                "Content-Type", "Content-Length", "Content-Disposition", "Content-Encoding", "Content-Language"
            };
            return contentHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsTransientStatus(HttpStatusCode code)
        {
            // Retry on 408, 429, 5xx
            var i = (int)code;
            if (i == 408 || i == 429) return true;
            if (i >= 500 && i < 600) return true;
            return false;
        }

        private static int ComputeBackoff(int attempt, int baseMs)
        {
            // exponential backoff with jitter
            var exp = Math.Pow(2, attempt - 1);
            var jitter = new Random().Next(0, baseMs);
            var delay = (int)(exp * baseMs) + jitter;
            return Math.Min(delay, 60_000); // cap at 60s
        }

        private static string? ExtractProviderMessageId(HttpResponseMessage resp)
        {
            // Common headers that may contain provider message id
            if (resp.Headers.TryGetValues("X-Message-Id", out var v1)) return v1.FirstOrDefault();
            if (resp.Headers.TryGetValues("X-Request-Id", out var v2)) return v2.FirstOrDefault();
            if (resp.Headers.Location != null) return resp.Headers.Location.ToString();
            return null;
        }

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "...";
        }

        #endregion
    }

    /// <summary>
    /// Options for WebhookSender. Bind from "Comms:Webhook" section.
    /// </summary>
    public class WebhookOptions
    {
        /// <summary>
        /// Optional named HttpClient to resolve from IHttpClientFactory.
        /// If empty, CreateClient(string.Empty) returns a default client.
        /// </summary>
        public string? HttpClientName { get; set; }

        /// <summary>
        /// Default headers to include on every webhook request.
        /// </summary>
        public IDictionary<string, string>? DefaultHeaders { get; set; }

        /// <summary>
        /// Maximum attempts (including first attempt). Default 3.
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Base backoff in milliseconds for exponential backoff. Default 200ms.
        /// </summary>
        public int BackoffBaseMs { get; set; } = 200;

        /// <summary>
        /// Per-request timeout. If null, HttpClient's default applies.
        /// </summary>
        public TimeSpan? RequestTimeout { get; set; }
    }
}
