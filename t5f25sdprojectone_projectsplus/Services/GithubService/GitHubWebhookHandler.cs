// src/ProjectsPlus.GitHub/Webhooks/GitHubWebhookHandler.cs
// Purpose: Validate GitHub webhook signatures and provide a small, testable handler to route events.
// Note: This file contains a handler class and an example minimal ASP.NET Core controller that uses it.
// Keep webhook secret in GithubCredsPlus.Webhook.Secret and never log raw payloads.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    /// <summary>
    /// Minimal contract for processing GitHub webhook events.
    /// Implementations should be idempotent and fast; heavy work should be queued to background workers.
    /// </summary>
    public interface IGitHubWebhookHandler
    {
        /// <summary>
        /// Validate signature and process the webhook payload.
        /// Returns true if processed successfully (or queued) and false if validation failed.
        /// </summary>
        Task<bool> HandleAsync(string signatureHeader, string eventName, string deliveryId, ReadOnlyMemory<byte> payload, CancellationToken ct = default);
    }

    /// <summary>
    /// Default webhook handler that validates HMAC signature and routes a few common events.
    /// It intentionally keeps processing lightweight: it parses the payload and enqueues or calls services as needed.
    /// Replace or extend routing logic to integrate with your background job system.
    /// </summary>
    public sealed class GitHubWebhookHandler : IGitHubWebhookHandler
    {
        private readonly IGithubCredsProvider _credsProvider;
        private readonly IGitHubHttpClientFactory _httpFactory;
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

        public GitHubWebhookHandler(IGithubCredsProvider credsProvider, IGitHubHttpClientFactory httpFactory, ILogger<GitHubWebhookHandler> logger)
        {
            _credsProvider = credsProvider ?? throw new ArgumentNullException(nameof(credsProvider));
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> HandleAsync(string signatureHeader, string eventName, string deliveryId, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            // Validate inputs
            if (string.IsNullOrWhiteSpace(eventName))
            {
                _logger.LogWarning("Webhook missing event name (delivery {Delivery})", deliveryId);
                return false;
            }

            var creds = await _credsProvider.GetCredsAsync(ct).ConfigureAwait(false);
            var secret = creds.Webhook?.Secret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                _logger.LogWarning("No webhook secret configured; rejecting webhook (delivery {Delivery})", deliveryId);
                return false;
            }

            // Validate signature
            if (!ValidateSignature(secret, payload.Span, signatureHeader))
            {
                _logger.LogWarning("Webhook signature validation failed for event {Event} (delivery {Delivery})", eventName, deliveryId);
                return false;
            }

            // Parse payload minimally (avoid heavy deserialization until we know what to do)
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                // Route a few common events conservatively
                switch (eventName)
                {
                    case "push":
                        await HandlePushAsync(root, deliveryId, ct).ConfigureAwait(false);
                        break;

                    case "pull_request":
                        await HandlePullRequestAsync(root, deliveryId, ct).ConfigureAwait(false);
                        break;

                    case "issues":
                        await HandleIssueAsync(root, deliveryId, ct).ConfigureAwait(false);
                        break;

                    case "issue_comment":
                        await HandleIssueCommentAsync(root, deliveryId, ct).ConfigureAwait(false);
                        break;

                    case "project":
                    case "project_card":
                    case "project_column":
                        // Projects events: queue reconciliation or refresh tasks
                        await HandleProjectEventAsync(root, eventName, deliveryId, ct).ConfigureAwait(false);
                        break;

                    default:
                        // Unknown or unhandled event: log and ignore (do not fail)
                        _logger.LogInformation("Received unhandled webhook event {Event} (delivery {Delivery})", eventName, deliveryId);
                        break;
                }

                return true;
            }
            catch (JsonException jex)
            {
                _logger.LogWarning(jex, "Failed to parse webhook JSON for event {Event} (delivery {Delivery})", eventName, deliveryId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while handling webhook {Event} (delivery {Delivery})", eventName, deliveryId);
                return false;
            }
        }

        #region Signature validation

        private bool ValidateSignature(string secret, ReadOnlySpan<byte> payload, string signatureHeader)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
                return _httpFactory.ValidateWebhookSignature(secret, payload, signatureHeader);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception during webhook signature validation");
                return false;
            }
        }

        #endregion

        #region Event handlers (lightweight)

        private Task HandlePushAsync(JsonElement payload, string deliveryId, CancellationToken ct)
        {
            // Example: extract repo full name and head commit id; enqueue a sync job
            try
            {
                var repoFull = payload.TryGetProperty("repository", out var r) && r.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;
                var headSha = payload.TryGetProperty("after", out var a) ? a.GetString() : null;
                _logger.LogInformation("Push event for {Repo} head {Sha} (delivery {Delivery})", repoFull ?? "<unknown>", headSha ?? "<unknown>", deliveryId);

                // TODO: enqueue background job to reconcile project cards, update contributions, etc.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling push event (delivery {Delivery})", deliveryId);
            }

            return Task.CompletedTask;
        }

        private Task HandlePullRequestAsync(JsonElement payload, string deliveryId, CancellationToken ct)
        {
            try
            {
                var action = payload.TryGetProperty("action", out var a) ? a.GetString() : null;
                var pr = payload.TryGetProperty("pull_request", out var p) ? p : default;
                var number = pr.ValueKind != JsonValueKind.Undefined && pr.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                var repoFull = payload.TryGetProperty("repository", out var r) && r.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;

                _logger.LogInformation("Pull request event {Action} for {Repo} PR#{Number} (delivery {Delivery})", action ?? "<unknown>", repoFull ?? "<unknown>", number, deliveryId);

                // TODO: enqueue PR reconciliation, update project card notes, recalc contributions if merged, etc.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling pull_request event (delivery {Delivery})", deliveryId);
            }

            return Task.CompletedTask;
        }

        private Task HandleIssueAsync(JsonElement payload, string deliveryId, CancellationToken ct)
        {
            try
            {
                var action = payload.TryGetProperty("action", out var a) ? a.GetString() : null;
                var issue = payload.TryGetProperty("issue", out var i) ? i : default;
                var number = issue.ValueKind != JsonValueKind.Undefined && issue.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                var repoFull = payload.TryGetProperty("repository", out var r) && r.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;

                _logger.LogInformation("Issue event {Action} for {Repo} Issue#{Number} (delivery {Delivery})", action ?? "<unknown>", repoFull ?? "<unknown>", number, deliveryId);

                // TODO: update linked project cards, contributions, etc.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling issues event (delivery {Delivery})", deliveryId);
            }

            return Task.CompletedTask;
        }

        private Task HandleIssueCommentAsync(JsonElement payload, string deliveryId, CancellationToken ct)
        {
            try
            {
                var action = payload.TryGetProperty("action", out var a) ? a.GetString() : null;
                var comment = payload.TryGetProperty("comment", out var c) ? c : default;
                var body = comment.ValueKind != JsonValueKind.Undefined && comment.TryGetProperty("body", out var b) ? b.GetString() : null;
                var repoFull = payload.TryGetProperty("repository", out var r) && r.TryGetProperty("full_name", out var fn) ? fn.GetString() : null;

                _logger.LogInformation("Issue comment event {Action} for {Repo} (delivery {Delivery})", action ?? "<unknown>", repoFull ?? "<unknown>", deliveryId);

                // TODO: if comment references ProjectsPlus commands, parse and act; otherwise ignore or queue.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling issue_comment event (delivery {Delivery})", deliveryId);
            }

            return Task.CompletedTask;
        }

        private Task HandleProjectEventAsync(JsonElement payload, string eventName, string deliveryId, CancellationToken ct)
        {
            try
            {
                // Project events are often noisy; log and queue reconciliation for the affected project/card.
                _logger.LogInformation("Project event {Event} received (delivery {Delivery})", eventName, deliveryId);
                // TODO: extract project id / card id and enqueue reconciliation.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error handling project event {Event} (delivery {Delivery})", eventName, deliveryId);
            }

            return Task.CompletedTask;
        }

        #endregion
    }

    #region Example ASP.NET Core controller

    /// <summary>
    /// Minimal example controller showing how to wire the webhook handler into ASP.NET Core.
    /// Register this controller in your app and configure routing to POST /api/github/webhook (or your chosen path).
    /// </summary>
    [ApiController]
    [Route("api/github/webhook")]
    public sealed class GitHubWebhookController : ControllerBase
    {
        private readonly IGitHubWebhookHandler _handler;
        private readonly ILogger _logger;

        public GitHubWebhookController(IGitHubWebhookHandler handler, ILogger<GitHubWebhookController> logger)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost]
        public async Task<IActionResult> Post(CancellationToken ct)
        {
            // Read headers
            var signature = Request.Headers["X-Hub-Signature-256"].ToString();
            var eventName = Request.Headers["X-GitHub-Event"].ToString();
            var delivery = Request.Headers["X-GitHub-Delivery"].ToString();

            // Read raw body as bytes (do not read as string to avoid encoding issues)
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct).ConfigureAwait(false);
            var payload = ms.ToArray();

            var ok = await _handler.HandleAsync(signature, eventName, delivery, payload, ct).ConfigureAwait(false);
            if (!ok)
            {
                // Return 401 for signature failure, 202 for accepted processing, 200 for handled
                if (string.IsNullOrWhiteSpace(signature))
                {
                    return Unauthorized();
                }

                // If validation failed or processing error, return 400 to indicate problem
                return BadRequest();
            }

            // Acknowledge quickly; heavy work should be queued
            return Accepted();
        }
    }

    #endregion
}
