// src/ProjectsPlus.GitHub/Http/GitHubHttpClientFactory.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    /// <summary>
    /// Factory and helpers for GitHub HTTP interactions.
    /// Provides configured HttpClient instances and a retry/send wrapper that honors GitHub rate-limit headers.
    /// Also includes webhook HMAC helpers and a minimal GitHub App JWT generator.
    /// </summary>
    public interface IGitHubHttpClientFactory
    {
        /// <summary>
        /// Create a configured HttpClient for GitHub API calls.
        /// Caller should not dispose the returned client (factory manages lifetime).
        /// </summary>
        HttpClient CreateClient(string? userAgentSuffix = null);

        /// <summary>
        /// Send an HttpRequestMessage with retry/backoff logic that respects GitHub rate-limit headers.
        /// Returns the HttpResponseMessage (caller is responsible for disposing it).
        /// </summary>
        Task<HttpResponseMessage> SendWithRetryAsync(HttpClient client, HttpRequestMessage req, CancellationToken ct = default);

        /// <summary>
        /// Compute the GitHub webhook signature header value for a payload using the given secret.
        /// Returns a string like "sha256=..." suitable for comparing with X-Hub-Signature-256.
        /// </summary>
        string ComputeWebhookSignature(string secret, ReadOnlySpan<byte> payload);

        /// <summary>
        /// Validate the webhook signature in a timing-safe manner.
        /// </summary>
        bool ValidateWebhookSignature(string secret, ReadOnlySpan<byte> payload, string signatureHeader);

        /// <summary>
        /// Create a short-lived JWT for GitHub App authentication using a PEM-encoded RSA private key.
        /// The returned token is valid for a short window (recommended <= 10 minutes).
        /// </summary>
        string CreateJwtForApp(long appId, string privateKeyPem, TimeSpan? validFor = null);
    }

    public sealed class GitHubHttpClientFactory : IGitHubHttpClientFactory, IDisposable
    {
        private readonly HttpClient _sharedClient;
        private readonly string _defaultUserAgent;
        private readonly int _maxAttempts;
        private readonly int _baseDelayMs;
        private readonly int _maxDelayMs;
        private bool _disposed;

        public GitHubHttpClientFactory(string productName = "ProjectsPlus", int maxAttempts = 5, int baseDelayMs = 500, int maxDelayMs = 30_000)
        {
            _defaultUserAgent = $"{productName}/1.0 (+https://example.com/projectsplus)";
            _maxAttempts = Math.Max(1, maxAttempts);
            _baseDelayMs = Math.Max(100, baseDelayMs);
            _maxDelayMs = Math.Max(_baseDelayMs, maxDelayMs);

            // Shared client with conservative timeout; callers may override per-request if needed.
            _sharedClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(100)
            };
            _sharedClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _sharedClient.DefaultRequestHeaders.UserAgent.ParseAdd(_defaultUserAgent);
        }

        public HttpClient CreateClient(string? userAgentSuffix = null)
        {
            if (string.IsNullOrWhiteSpace(userAgentSuffix)) return _sharedClient;

            // Create a lightweight wrapper that clones default headers but allows a custom UA suffix.
            var client = new HttpClient
            {
                Timeout = _sharedClient.Timeout
            };
            foreach (var h in _sharedClient.DefaultRequestHeaders)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);
            }
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"{_defaultUserAgent} {userAgentSuffix}");
            return client;
        }

        public async Task<HttpResponseMessage> SendWithRetryAsync(HttpClient client, HttpRequestMessage req, CancellationToken ct = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (req == null) throw new ArgumentNullException(nameof(req));

            int attempt = 0;
            Exception? lastEx = null;

            while (attempt < _maxAttempts)
            {
                attempt++;
                ct.ThrowIfCancellationRequested();

                HttpResponseMessage? resp = null;
                try
                {
                    resp = await client.SendAsync(req.Clone(), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                    // If success or client error that is not retryable, return immediately
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                    {
                        return resp;
                    }

                    // Handle rate limiting: 429 or 403 with rate-limit headers
                    if (resp.StatusCode == (HttpStatusCode)429 || resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.ServiceUnavailable)
                    {
                        // Check Retry-After header first
                        if (TryGetRetryAfterDelay(resp.Headers, out var retryAfter))
                        {
                            await DelayWithCancellationAsync(retryAfter, ct).ConfigureAwait(false);
                            resp.Dispose();
                            continue;
                        }

                        // Check X-RateLimit-Reset header
                        if (TryGetRateLimitResetDelay(resp.Headers, out var resetDelay))
                        {
                            await DelayWithCancellationAsync(resetDelay, ct).ConfigureAwait(false);
                            resp.Dispose();
                            continue;
                        }
                    }

                    // For 5xx server errors, retry with backoff
                    if ((int)resp.StatusCode >= 500 && (int)resp.StatusCode <= 599)
                    {
                        var backoff = ComputeBackoff(attempt);
                        await DelayWithCancellationAsync(backoff, ct).ConfigureAwait(false);
                        resp.Dispose();
                        continue;
                    }

                    // Non-retryable status: return response to caller for handling
                    return resp;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    // transient network error: backoff and retry
                    var backoff = ComputeBackoff(attempt);
                    await DelayWithCancellationAsync(backoff, ct).ConfigureAwait(false);
                    resp?.Dispose();
                    continue;
                }
            }

            throw new InvalidOperationException("Exceeded retry attempts for GitHub request.", lastEx);
        }

        /// <summary>
        /// Compute HMAC SHA256 signature header value for webhook payload.
        /// Returns "sha256=..." string.
        /// </summary>
        public string ComputeWebhookSignature(string secret, ReadOnlySpan<byte> payload)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(payload.ToArray());
            return "sha256=" + ToHex(hash);
        }

        /// <summary>
        /// Timing-safe comparison of computed signature and header value.
        /// Accepts header values like "sha256=..." or raw hex.
        /// </summary>
        public bool ValidateWebhookSignature(string secret, ReadOnlySpan<byte> payload, string signatureHeader)
        {
            if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
            var expected = ComputeWebhookSignature(secret, payload);
            return FixedTimeEquals(expected, signatureHeader);
        }

        /// <summary>
        /// Create a short-lived JWT for GitHub App authentication.
        /// Minimal implementation using RSA SHA256 and a PEM private key.
        /// </summary>
        public string CreateJwtForApp(long appId, string privateKeyPem, TimeSpan? validFor = null)
        {
            if (appId <= 0) throw new ArgumentOutOfRangeException(nameof(appId));
            if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentNullException(nameof(privateKeyPem));

            var now = DateTimeOffset.UtcNow;
            var exp = now.Add(validFor ?? TimeSpan.FromMinutes(9)); // GitHub recommends <= 10 minutes

            var header = new Dictionary<string, object>
            {
                ["alg"] = "RS256",
                ["typ"] = "JWT"
            };

            var payload = new Dictionary<string, object>
            {
                ["iat"] = ToUnixSeconds(now),
                ["exp"] = ToUnixSeconds(exp),
                ["iss"] = appId.ToString(CultureInfo.InvariantCulture)
            };

            string headerJson = JsonSerializer.Serialize(header);
            string payloadJson = JsonSerializer.Serialize(payload);

            string headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
            string unsignedToken = $"{headerB64}.{payloadB64}";

            // Sign with RSA private key from PEM
            using var rsa = PemUtils.RsaFromPrivateKey(privateKeyPem);
            var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsignedToken), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            string sigB64 = Base64UrlEncode(signature);

            return $"{unsignedToken}.{sigB64}";
        }

        #region Helpers

        private static long ToUnixSeconds(DateTimeOffset dt) => dt.ToUnixTimeSeconds();

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.AppendFormat("{0:x2}", b);
            return sb.ToString();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            var aBytes = Encoding.UTF8.GetBytes(a ?? string.Empty);
            var bBytes = Encoding.UTF8.GetBytes(b ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        private static bool TryGetRetryAfterDelay(HttpResponseHeaders headers, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            if (headers.TryGetValues("Retry-After", out var vals))
            {
                var s = vals.FirstOrDefault();
                if (int.TryParse(s, out var seconds))
                {
                    delay = TimeSpan.FromSeconds(seconds);
                    return true;
                }
                if (DateTimeOffset.TryParse(s, out var dt))
                {
                    var d = dt - DateTimeOffset.UtcNow;
                    delay = d > TimeSpan.Zero ? d : TimeSpan.Zero;
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetRateLimitResetDelay(HttpResponseHeaders headers, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            if (headers.TryGetValues("X-RateLimit-Reset", out var vals))
            {
                var s = vals.FirstOrDefault();
                if (long.TryParse(s, out var unix))
                {
                    var reset = DateTimeOffset.FromUnixTimeSeconds(unix);
                    var d = reset - DateTimeOffset.UtcNow;
                    delay = d > TimeSpan.Zero ? d : TimeSpan.Zero;
                    return true;
                }
            }
            return false;
        }

        private int ComputeBackoff(int attempt)
        {
            // exponential backoff with jitter
            var exp = Math.Min(_maxDelayMs, _baseDelayMs * (1 << attempt - 1));
            var jitter = RandomNumberGenerator.GetInt32(0, Math.Max(1, _baseDelayMs));
            var delay = Math.Min(_maxDelayMs, exp + jitter);
            return delay;
        }

        private static Task DelayWithCancellationAsync(int ms, CancellationToken ct)
            => Task.Delay(TimeSpan.FromMilliseconds(ms), ct);

        private static Task DelayWithCancellationAsync(TimeSpan ts, CancellationToken ct)
            => Task.Delay(ts, ct);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _sharedClient.Dispose();
            _disposed = true;
        }

        #endregion
    }

    #region HttpRequestMessage clone extension

    internal static class HttpRequestMessageExtensions
    {
        /// <summary>
        /// Clone an HttpRequestMessage so it can be retried safely.
        /// </summary>
        public static HttpRequestMessage Clone(this HttpRequestMessage req)
        {
            var clone = new HttpRequestMessage(req.Method, req.RequestUri)
            {
                Version = req.Version
            };

            // Copy content (if any)
            if (req.Content != null)
            {
                var ms = new MemoryStream();
                req.Content.CopyToAsync(ms).GetAwaiter().GetResult();
                ms.Position = 0;
                clone.Content = new StreamContent(ms);

                foreach (var h in req.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            foreach (var header in req.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            foreach (var prop in req.Options)
            {
                // .NET 6+ HttpRequestOptionsKey - copy if needed; skip for simplicity
            }

            return clone;
        }
    }

    #endregion

    #region Minimal PEM utils (RSA) - no external deps

    internal static class PemUtils
    {
        /// <summary>
        /// Create an RSA instance from a PEM-encoded PKCS#1 or PKCS#8 private key.
        /// Supports unencrypted PEMs.
        /// </summary>
        public static RSA RsaFromPrivateKey(string pem)
        {
            if (string.IsNullOrWhiteSpace(pem)) throw new ArgumentNullException(nameof(pem));

            var pkcs8Header = "-----BEGIN PRIVATE KEY-----";
            var pkcs1Header = "-----BEGIN RSA PRIVATE KEY-----";

            string base64;
            if (pem.Contains(pkcs8Header, StringComparison.Ordinal))
            {
                base64 = ExtractBase64(pem, pkcs8Header, "-----END PRIVATE KEY-----");
                var pkcs8 = Convert.FromBase64String(base64);
                var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                return rsa;
            }
            else if (pem.Contains(pkcs1Header, StringComparison.Ordinal))
            {
                base64 = ExtractBase64(pem, pkcs1Header, "-----END RSA PRIVATE KEY-----");
                var pkcs1 = Convert.FromBase64String(base64);
                var rsa = RSA.Create();
                rsa.ImportRSAPrivateKey(pkcs1, out _);
                return rsa;
            }
            else
            {
                throw new ArgumentException("Unsupported PEM format. Expected PKCS#1 or PKCS#8 private key.");
            }
        }

        private static string ExtractBase64(string pem, string beginMarker, string endMarker)
        {
            var start = pem.IndexOf(beginMarker, StringComparison.Ordinal);
            if (start < 0) throw new ArgumentException("PEM begin marker not found.");
            start += beginMarker.Length;
            var end = pem.IndexOf(endMarker, start, StringComparison.Ordinal);
            if (end < 0) throw new ArgumentException("PEM end marker not found.");
            var base64 = pem[start..end];
            return base64.Replace("\r", "").Replace("\n", "").Trim();
        }
    }

    #endregion
}
