// src/ProjectsPlus.GitHub/GitHubManager.Repos.cs
// Partial implementation: repository lifecycle and branch protection
// Implements IGitHubRepositoryManager using IGithubCredsProvider and IGitHubHttpClientFactory
// NOTE: This file is intentionally conservative and uses only GitHub REST endpoints available on free plans.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    public partial class GitHubManager : IGitHubRepositoryManager, IDisposable
    {
        private readonly IGithubCredsProvider _credsProvider;
        private readonly IGitHubHttpClientFactory _httpFactory;
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonOpts;
        private HttpClient? _client; // lazily created per auth token; not disposed per request

        public GitHubManager(IGithubCredsProvider credsProvider, IGitHubHttpClientFactory httpFactory, ILogger<GitHubManager> logger)
        {
            _credsProvider = credsProvider ?? throw new ArgumentNullException(nameof(credsProvider));
            _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        #region IGitHubRepositoryManager

        public async Task<RepositoryDto> CreateRepositoryAsync(RepositoryDto repoSpec, bool initializeWithReadme = true, CancellationToken ct = default)
        {
            if (repoSpec == null) throw new ArgumentNullException(nameof(repoSpec));
            if (string.IsNullOrWhiteSpace(repoSpec.Name)) throw new ArgumentException("Repository name must be provided.", nameof(repoSpec));

            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            // Determine endpoint: org or user
            string endpoint;
            bool isOrg = !string.IsNullOrWhiteSpace(repoSpec.Owner);
            if (isOrg)
            {
                endpoint = $"https://api.github.com/orgs/{Uri.EscapeDataString(repoSpec.Owner)}/repos";
            }
            else
            {
                endpoint = "https://api.github.com/user/repos";
            }

            var payload = new Dictionary<string, object?>
            {
                ["name"] = repoSpec.Name,
                ["description"] = repoSpec.Description,
                ["private"] = repoSpec.Visibility != RepositoryVisibility.Public,
                ["auto_init"] = initializeWithReadme,
                ["has_issues"] = true,
                ["has_projects"] = true,
                ["has_wiki"] = true
            };

            var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOpts), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOpts);
                    return MapRepoFromJson(doc);
                }
                else
                {
                    var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogError("CreateRepositoryAsync failed: {Status} {Body}", resp.StatusCode, err);
                    throw new InvalidOperationException($"GitHub Create repository failed: {resp.StatusCode} - {err}");
                }
            }
        }

        public async Task<RepositoryDto?> GetRepositoryAsync(string owner, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOpts);
                    return MapRepoFromJson(doc);
                }
                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogWarning("GetRepositoryAsync unexpected status {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
        }

        public async Task<PagedResult<RepositoryDto>> ListRepositoriesAsync(string ownerOrOrg, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ownerOrOrg)) throw new ArgumentNullException(nameof(ownerOrOrg));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            // Try org repos first; if 404, fallback to user repos
            var url = $"https://api.github.com/orgs/{Uri.EscapeDataString(ownerOrOrg)}/repos?per_page={pageSize}";
            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                url += $"&page={Uri.EscapeDataString(continuationToken)}";
            }

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // fallback to user repos
                    url = $"https://api.github.com/users/{Uri.EscapeDataString(ownerOrOrg)}/repos?per_page={pageSize}";
                    if (!string.IsNullOrWhiteSpace(continuationToken))
                    {
                        url += $"&page={Uri.EscapeDataString(continuationToken)}";
                    }
                    var req2 = new HttpRequestMessage(HttpMethod.Get, url);
                    resp = await _httpFactory.SendWithRetryAsync(client, req2, ct).ConfigureAwait(false);
                }

                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOpts);
                    var list = new List<RepositoryDto>();
                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            list.Add(MapRepoFromJson(item));
                        }
                    }

                    // Try to parse Link header for next page token (simple heuristic)
                    string? nextToken = null;
                    if (resp.Headers.TryGetValues("Link", out var linkVals))
                    {
                        var link = linkVals.FirstOrDefault();
                        // parse rel="next" page param
                        var next = ParseNextPageFromLinkHeader(link);
                        if (!string.IsNullOrWhiteSpace(next)) nextToken = next;
                    }

                    return new PagedResult<RepositoryDto> { Items = list, ContinuationToken = nextToken, TotalCount = list.Count };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("ListRepositoriesAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"GitHub list repos failed: {resp.StatusCode} - {err}");
            }
        }

        public async Task<OperationResult> DeleteRepositoryAsync(string owner, string name, string requestedBy, bool purgeProjectsPlusRecords = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrWhiteSpace(requestedBy)) throw new ArgumentNullException(nameof(requestedBy));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    // Deletion accepted. Purge of ProjectsPlus records is handled by IPurgeService elsewhere.
                    return new OperationResult { Success = true, Message = "Repository deleted on GitHub" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Repository not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("DeleteRepositoryAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"GitHub delete failed: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        public async Task<OperationResult> ProtectBranchAsync(string owner, string repo, BranchProtectionDto protection, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (protection == null) throw new ArgumentNullException(nameof(protection));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches/{Uri.EscapeDataString(protection.BranchName)}/protection";

            // Build protection payload according to GitHub API (keep minimal to remain free-tier compatible)
            var payload = new Dictionary<string, object?>
            {
                ["required_status_checks"] = protection.RequireStatusChecks
                    ? new Dictionary<string, object?> { ["strict"] = true, ["contexts"] = protection.RequiredStatusCheckContexts ?? Array.Empty<string>() }
                    : null,
                ["enforce_admins"] = protection.EnforceAdmins,
                ["required_pull_request_reviews"] = new Dictionary<string, object?>
                {
                    ["dismiss_stale_reviews"] = false,
                    ["require_code_owner_reviews"] = false,
                    ["required_approving_review_count"] = Math.Max(1, protection.RequiredApprovingReviewCount)
                },
                ["restrictions"] = protection.RestrictPushTo != null && protection.RestrictPushTo.Count > 0
                    ? new Dictionary<string, object?> { ["users"] = protection.RestrictPushTo, ["teams"] = Array.Empty<string>() }
                    : null
            };

            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOpts), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    return new OperationResult { Success = true, Message = "Branch protection applied" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("ProtectBranchAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to apply branch protection: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        public async Task<BranchProtectionDto?> GetBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches/{Uri.EscapeDataString(branch)}/protection";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOpts);

                    var bp = new BranchProtectionDto
                    {
                        BranchName = branch,
                        EnforceAdmins = doc.TryGetProperty("enforce_admins", out var ea) && ea.GetBoolean(),
                        RequireStatusChecks = doc.TryGetProperty("required_status_checks", out var rsc) && rsc.ValueKind != JsonValueKind.Null,
                        RequiredStatusCheckContexts = doc.TryGetProperty("required_status_checks", out var rsc2) && rsc2.ValueKind != JsonValueKind.Null && rsc2.TryGetProperty("contexts", out var ctx)
                            ? JsonArrayToStringList(ctx)
                            : null,
                        RequirePullRequest = doc.TryGetProperty("required_pull_request_reviews", out var prr) && prr.ValueKind != JsonValueKind.Null,
                        RequiredApprovingReviewCount = doc.TryGetProperty("required_pull_request_reviews", out var prr2) && prr2.ValueKind != JsonValueKind.Null && prr2.TryGetProperty("required_approving_review_count", out var rac)
                            ? rac.GetInt32()
                            : 1
                    };

                    return bp;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogWarning("GetBranchProtectionAsync unexpected status {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
        }

        public async Task<OperationResult> RemoveBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/branches/{Uri.EscapeDataString(branch)}/protection";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = "Branch protection removed" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Branch protection not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("RemoveBranchProtectionAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to remove branch protection: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        #endregion

        #region Helpers

        private RepositoryDto MapRepoFromJson(JsonElement doc)
        {
            var owner = doc.TryGetProperty("owner", out var ownerElem) && ownerElem.TryGetProperty("login", out var login) ? login.GetString() ?? string.Empty : string.Empty;
            var name = doc.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? string.Empty : string.Empty;
            var createdAt = doc.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal) : DateTime.UtcNow;
            var html = doc.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
            var id = doc.TryGetProperty("id", out var idElem) && idElem.TryGetInt64(out var idVal) ? idVal : 0L;
            var visibility = RepositoryVisibility.Private;
            if (doc.TryGetProperty("private", out var priv) && priv.ValueKind == JsonValueKind.True) visibility = RepositoryVisibility.Private;
            else visibility = RepositoryVisibility.Public;

            return new RepositoryDto
            {
                Owner = owner,
                Name = name,
                Description = doc.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                Visibility = visibility,
                DefaultBranch = doc.TryGetProperty("default_branch", out var db) ? db.GetString() ?? "main" : "main",
                HtmlUrl = string.IsNullOrWhiteSpace(html) ? null : new Uri(html!),
                CreatedAt = createdAt,
                RepoId = id
            };
        }

        private static List<string> JsonArrayToStringList(JsonElement arr)
        {
            var list = new List<string>();
            if (arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var it in arr.EnumerateArray())
            {
                if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString() ?? string.Empty);
            }
            return list;
        }

        private static string? ParseNextPageFromLinkHeader(string? linkHeader)
        {
            if (string.IsNullOrWhiteSpace(linkHeader)) return null;
            // Link: <https://api.github.com/...&page=2>; rel="next", <...&page=5>; rel="last"
            var parts = linkHeader.Split(',');
            foreach (var p in parts)
            {
                var seg = p.Trim();
                if (seg.EndsWith("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                {
                    var start = seg.IndexOf('<');
                    var end = seg.IndexOf('>');
                    if (start >= 0 && end > start)
                    {
                        var url = seg.Substring(start + 1, end - start - 1);
                        var q = new Uri(url).Query;
                        var qs = System.Web.HttpUtility.ParseQueryString(q);
                        return qs["page"];
                    }
                }
            }
            return null;
        }

        private HttpClient CreateAuthClient(string token)
        {
            // Create a client with Authorization header set. Use factory to get base client defaults.
            var client = _httpFactory.CreateClient("GitHubManager");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
            return client;
        }

        private async Task<string> GetAuthTokenAsync(CancellationToken ct)
        {
            // Prefer GitHub App installation token if App creds provided and installation id present.
            var creds = await _credsProvider.GetCredsAsync(ct).ConfigureAwait(false);

            if (creds.App != null && creds.App.InstallationId.HasValue)
            {
                // Create JWT and exchange for installation access token
                var jwt = _httpFactory.CreateJwtForApp(creds.App.AppId, creds.App.PrivateKeyPem, TimeSpan.FromMinutes(9));
                using var client = _httpFactory.CreateClient("AppAuth");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                var url = $"https://api.github.com/app/installations/{creds.App.InstallationId.Value}/access_tokens";
                var req = new HttpRequestMessage(HttpMethod.Post, url);
                var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
                using (resp)
                {
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOpts);
                        if (doc.TryGetProperty("token", out var tok)) return tok.GetString() ?? throw new InvalidOperationException("Empty installation token");
                        throw new InvalidOperationException("Installation token not found in response.");
                    }
                    var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogError("Failed to obtain installation token: {Status} {Body}", resp.StatusCode, err);
                    throw new InvalidOperationException($"Failed to obtain installation token: {resp.StatusCode} - {err}");
                }
            }

            // Fallback to PAT if present
            var pat = creds.Pat;
            if (pat != null && !string.IsNullOrWhiteSpace(pat.Token))
            {
                return pat.Token;
            }

            // As a last resort, if OAuth is configured and a token exchange flow exists, implement it here.
            throw new InvalidOperationException("No usable GitHub credentials available (App with installationId or PAT required).");
        }

        private static async Task<string> SafeReadStringAsync(HttpResponseMessage resp)
        {
            try
            {
                return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            _client?.Dispose();
        }

        #endregion
    }
}
