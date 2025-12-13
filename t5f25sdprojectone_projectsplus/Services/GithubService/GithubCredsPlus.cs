using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    /// <summary>
    /// Central credentials/config model for GitHub integrations.
    /// Store instances securely (secrets vault / protected configuration).
    /// </summary>
    public sealed class GithubCredsPlus
    {
        /// <summary>
        /// Optional GitHub App credentials. Preferred for production (fine-grained permissions).
        /// </summary>
        [JsonPropertyName("app")]
        public GitHubAppCredentials? App { get; init; }

        /// <summary>
        /// Optional OAuth client credentials (if using OAuth flows for user tokens).
        /// </summary>
        [JsonPropertyName("oauth")]
        public OAuthCredentials? OAuth { get; init; }

        /// <summary>
        /// Optional Personal Access Token fallback for quick development or single-account automation.
        /// Should be avoided in production.
        /// </summary>
        [JsonPropertyName("pat")]
        public PatCredentials? Pat { get; init; }

        /// <summary>
        /// Webhook secret used to validate incoming GitHub webhooks.
        /// </summary>
        [JsonPropertyName("webhook")]
        public WebhookSettings? Webhook { get; init; }

        /// <summary>
        /// Default organization or owner to operate under when not explicitly provided.
        /// </summary>
        [JsonPropertyName("defaultOwner")]
        public string? DefaultOwner { get; init; }

        /// <summary>
        /// Default repository visibility for created repos.
        /// </summary>
        [JsonPropertyName("defaultVisibility")]
        public string? DefaultVisibility { get; init; } = "private";

        /// <summary>
        /// Scopes to request or ensure when creating tokens (informational).
        /// </summary>
        [JsonPropertyName("defaultScopes")]
        public IReadOnlyList<string> DefaultScopes { get; init; } = new[] { "repo", "workflow" };

        /// <summary>
        /// Optional proxy or HTTP settings for GitHub API calls.
        /// </summary>
        [JsonPropertyName("http")]
        public HttpSettings? Http { get; init; }

        /// <summary>
        /// Retry/backoff policy for GitHub API calls.
        /// </summary>
        [JsonPropertyName("retry")]
        public RetryPolicy? Retry { get; init; }

        /// <summary>
        /// Optional metadata for auditing or tagging created resources.
        /// </summary>
        [JsonPropertyName("metadata")]
        public IDictionary<string, string>? Metadata { get; init; }

        /// <summary>
        /// Validate minimal configuration. Throws ArgumentException on invalid state.
        /// Implementations should call this before using the credentials.
        /// </summary>
        public void ValidateForUse()
        {
            if (App == null && OAuth == null && Pat == null)
                throw new ArgumentException("At least one credential type must be provided: App, OAuth, or PAT.");

            if (App != null)
            {
                if (App.AppId <= 0) throw new ArgumentException("App.AppId must be set for GitHub App usage.");
                if (string.IsNullOrWhiteSpace(App.PrivateKeyPem)) throw new ArgumentException("App.PrivateKeyPem must be provided for GitHub App usage.");
            }

            if (OAuth != null)
            {
                if (string.IsNullOrWhiteSpace(OAuth.ClientId) || string.IsNullOrWhiteSpace(OAuth.ClientSecret))
                    throw new ArgumentException("OAuth ClientId and ClientSecret must be provided together.");
            }

            if (Pat != null)
            {
                if (string.IsNullOrWhiteSpace(Pat.Token)) throw new ArgumentException("PAT token must not be empty.");
            }
        }
    }

    public sealed class GitHubAppCredentials
    {
        /// <summary>
        /// Numeric GitHub App id.
        /// </summary>
        [JsonPropertyName("appId")]
        public long AppId { get; init; }

        /// <summary>
        /// PEM-encoded private key for the GitHub App. Store encrypted.
        /// </summary>
        [JsonPropertyName("privateKeyPem")]
        public string PrivateKeyPem { get; init; } = string.Empty;

        /// <summary>
        /// Optional App slug (human-friendly name).
        /// </summary>
        [JsonPropertyName("slug")]
        public string? Slug { get; init; }

        /// <summary>
        /// Optional installation id to target a specific org/account. If null, implementations should perform installation discovery.
        /// </summary>
        [JsonPropertyName("installationId")]
        public long? InstallationId { get; init; }

        /// <summary>
        /// Optional PEM password if the private key is encrypted (rare).
        /// </summary>
        [JsonPropertyName("privateKeyPassword")]
        public string? PrivateKeyPassword { get; init; }
    }

    public sealed class OAuthCredentials
    {
        [JsonPropertyName("clientId")]
        public string ClientId { get; init; } = string.Empty;

        [JsonPropertyName("clientSecret")]
        public string ClientSecret { get; init; } = string.Empty;

        /// <summary>
        /// Optional redirect URI used in OAuth flows.
        /// </summary>
        [JsonPropertyName("redirectUri")]
        public string? RedirectUri { get; init; }
    }

    public sealed class PatCredentials
    {
        /// <summary>
        /// Personal Access Token string. Store encrypted and rotate regularly.
        /// </summary>
        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;

        /// <summary>
        /// Optional expiry if known.
        /// </summary>
        [JsonPropertyName("expiresAt")]
        public DateTimeOffset? ExpiresAt { get; init; }

        /// <summary>
        /// Optional owner/account this PAT belongs to (for auditing).
        /// </summary>
        [JsonPropertyName("owner")]
        public string? Owner { get; init; }
    }

    public sealed class WebhookSettings
    {
        /// <summary>
        /// Secret used to validate webhook payload signatures (HMAC SHA256).
        /// </summary>
        [JsonPropertyName("secret")]
        public string Secret { get; init; } = string.Empty;

        /// <summary>
        /// Optional list of events to subscribe to. If null, use a sensible default.
        /// </summary>
        [JsonPropertyName("events")]
        public IReadOnlyList<string>? Events { get; init; }

        /// <summary>
        /// Optional endpoint path (relative) where webhooks will be delivered.
        /// </summary>
        [JsonPropertyName("endpointPath")]
        public string? EndpointPath { get; init; } = "/api/github/webhook";
    }

    public sealed class HttpSettings
    {
        [JsonPropertyName("proxyUrl")]
        public string? ProxyUrl { get; init; }

        [JsonPropertyName("timeoutSeconds")]
        public int TimeoutSeconds { get; init; } = 100;

        [JsonPropertyName("userAgent")]
        public string? UserAgent { get; init; }
    }

    public sealed class RetryPolicy
    {
        /// <summary>
        /// Maximum number of retry attempts for transient GitHub API errors.
        /// </summary>
        [JsonPropertyName("maxAttempts")]
        public int MaxAttempts { get; init; } = 5;

        /// <summary>
        /// Base delay in milliseconds for exponential backoff.
        /// </summary>
        [JsonPropertyName("baseDelayMs")]
        public int BaseDelayMs { get; init; } = 500;

        /// <summary>
        /// Maximum delay in milliseconds between retries.
        /// </summary>
        [JsonPropertyName("maxDelayMs")]
        public int MaxDelayMs { get; init; } = 30_000;

        /// <summary>
        /// Whether to honor GitHub Retry-After header when present.
        /// </summary>
        [JsonPropertyName("honorRetryAfter")]
        public bool HonorRetryAfter { get; init; } = true;
    }
}
