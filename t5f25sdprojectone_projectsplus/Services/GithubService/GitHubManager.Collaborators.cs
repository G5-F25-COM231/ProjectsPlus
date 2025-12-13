// src/ProjectsPlus.GitHub/GitHubManager.Collaborators.cs
// Partial implementation: collaborator management and basic collaborator listing.
// Implements ICollaboratorService using IGithubCredsProvider and IGitHubHttpClientFactory.
// Uses GitHub REST API endpoints available on free tier.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    public partial class GitHubManager : ICollaboratorService
    {
        private readonly JsonSerializerOptions _jsonOptsCollab = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Invite a collaborator to a repository. If usernameOrEmail looks like an email, GitHub will send an email invite.
        /// Permission maps to GitHub permission strings: read, triage, write, maintain, admin.
        /// </summary>
        public async Task<OperationResult> InviteCollaboratorAsync(string owner, string repo, string usernameOrEmail, CollaboratorPermission permission, TimeSpan? inviteExpiry = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(usernameOrEmail)) throw new ArgumentNullException(nameof(usernameOrEmail));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/collaborators/{Uri.EscapeDataString(usernameOrEmail)}";
            var payload = new Dictionary<string, object?>
            {
                ["permission"] = MapPermission(permission)
            };

            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsCollab), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                // 201 Created = invitation created; 204 No Content = user already a collaborator
                if (resp.StatusCode == System.Net.HttpStatusCode.Created || resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = resp.StatusCode == System.Net.HttpStatusCode.Created ? "Invitation created" : "User added as collaborator" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("InviteCollaboratorAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to invite collaborator: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        /// <summary>
        /// Remove a collaborator from a repository.
        /// </summary>
        public async Task<OperationResult> RemoveCollaboratorAsync(string owner, string repo, string username, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/collaborators/{Uri.EscapeDataString(username)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = "Collaborator removed" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Collaborator or repository not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("RemoveCollaboratorAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to remove collaborator: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        /// <summary>
        /// List collaborators for a repository. Returns username, permission and invite status where available.
        /// Uses the REST endpoint: GET /repos/{owner}/{repo}/collaborators
        /// </summary>
        public async Task<IReadOnlyList<CollaboratorDto>> ListCollaboratorsAsync(string owner, string repo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/collaborators?per_page=100";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Array.Empty<CollaboratorDto>();
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsCollab);
                    var list = new List<CollaboratorDto>();
                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            var username = it.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                            // Permission is not returned in this list by default; need to call /repos/{owner}/{repo}/collaborators/{username}/permission
                            var dto = new CollaboratorDto
                            {
                                Username = username,
                                Permission = CollaboratorPermission.Read,
                                IsPendingInvite = false,
                                InvitedAt = null
                            };
                            list.Add(dto);
                        }
                    }

                    // Enrich with permission per user (best-effort, limited to returned users)
                    foreach (var dto in list.ToArray())
                    {
                        try
                        {
                            var perm = await GetCollaboratorPermissionAsync(owner, repo, dto.Username, ct).ConfigureAwait(false);
                            dto.Permission = perm;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to fetch permission for {User}", dto.Username);
                        }
                    }

                    return list;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("ListCollaboratorsAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to list collaborators: {resp.StatusCode} - {err}");
            }
        }

        /// <summary>
        /// Add a team to a repository with a permission. Requires org context.
        /// Endpoint: PUT /orgs/{org}/teams/{team_slug}/repos/{owner}/{repo}
        /// </summary>
        public async Task<OperationResult> AddTeamToRepoAsync(string org, string teamSlug, string repo, CollaboratorPermission permission, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(org)) throw new ArgumentNullException(nameof(org));
            if (string.IsNullOrWhiteSpace(teamSlug)) throw new ArgumentNullException(nameof(teamSlug));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/orgs/{Uri.EscapeDataString(org)}/teams/{Uri.EscapeDataString(teamSlug)}/repos/{Uri.EscapeDataString(org)}/{Uri.EscapeDataString(repo)}";
            var payload = new { permission = MapPermission(permission) };

            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsCollab), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent || (int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    return new OperationResult { Success = true, Message = "Team added to repository" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("AddTeamToRepoAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to add team to repo: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        #region Internal helpers

        private static string MapPermission(CollaboratorPermission p) =>
            p switch
            {
                CollaboratorPermission.Read => "pull",
                CollaboratorPermission.Triage => "triage",
                CollaboratorPermission.Write => "push",
                CollaboratorPermission.Maintain => "maintain",
                CollaboratorPermission.Admin => "admin",
                _ => "pull"
            };

        private async Task<CollaboratorPermission> GetCollaboratorPermissionAsync(string owner, string repo, string username, CancellationToken ct)
        {
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);

            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/collaborators/{Uri.EscapeDataString(username)}/permission";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsCollab);
                    if (doc.TryGetProperty("permission", out var perm) && perm.ValueKind == JsonValueKind.String)
                    {
                        var p = perm.GetString() ?? "pull";
                        return p switch
                        {
                            "admin" => CollaboratorPermission.Admin,
                            "maintain" => CollaboratorPermission.Maintain,
                            "push" => CollaboratorPermission.Write,
                            "triage" => CollaboratorPermission.Triage,
                            _ => CollaboratorPermission.Read
                        };
                    }
                }

                // Default to Read on error
                return CollaboratorPermission.Read;
            }
        }

        #endregion
    }
}
