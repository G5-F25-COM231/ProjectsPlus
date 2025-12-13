// src/ProjectsPlus.GitHub/GitHubManager.Projects.cs
// Partial implementation: project board and card CRUD (classic Projects via REST).
// Note: This implementation targets GitHub classic Projects REST API (available on free tier).
// Projects v2 (GraphQL) is not implemented here; callers should use classic projects for now.

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
    public partial class GitHubManager : IProjectBoardService
    {
        private const string ProjectsPreviewAccept = "application/vnd.github.inertia-preview+json";
        private readonly JsonSerializerOptions _jsonOptsProjects = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        #region Projects (classic) - Project lifecycle

        public async Task<ProjectDto> CreateProjectAsync(string name, ProjectType type, string? repositoryFullName = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            ct.ThrowIfCancellationRequested();

            // Only classic projects supported here
            if (type != ProjectType.Classic) throw new NotSupportedException("Only classic Projects are supported by this implementation.");

            // If repositoryFullName provided, create project scoped to repo; otherwise create a user/org project (use default owner if configured)
            string owner = null!;
            string repo = null!;
            bool repoScoped = false;
            if (!string.IsNullOrWhiteSpace(repositoryFullName))
            {
                var parts = repositoryFullName.Split('/');
                if (parts.Length != 2) throw new ArgumentException("repositoryFullName must be in 'owner/repo' format.");
                owner = parts[0];
                repo = parts[1];
                repoScoped = true;
            }

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            string url;
            if (repoScoped)
            {
                url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/projects";
            }
            else
            {
                // create a user project under authenticated user
                url = "https://api.github.com/user/projects";
            }

            var payload = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["body"] = $"Project created by ProjectsPlus at {DateTime.UtcNow:O}"
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                    return MapProjectFromJson(doc, ProjectType.Classic, repositoryFullName);
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("CreateProjectAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to create project: {resp.StatusCode} - {err}");
            }
        }

        public async Task<ProjectDto?> GetProjectAsync(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            // projectId for classic projects is numeric id (string). Use /projects/{project_id}
            var url = $"https://api.github.com/projects/{Uri.EscapeDataString(projectId)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                    return MapProjectFromJson(doc, ProjectType.Classic, null);
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogWarning("GetProjectAsync unexpected status {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
        }

        public async Task<OperationResult> DeleteProjectAsync(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/{Uri.EscapeDataString(projectId)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = "Project deleted" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Project not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("DeleteProjectAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to delete project: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        #endregion

        #region Columns

        public async Task<ProjectColumnDto> CreateColumnAsync(string projectId, string columnName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            if (string.IsNullOrWhiteSpace(columnName)) throw new ArgumentNullException(nameof(columnName));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/{Uri.EscapeDataString(projectId)}/columns";
            var payload = new { name = columnName };
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                    return new ProjectColumnDto
                    {
                        ColumnId = doc.TryGetProperty("id", out var id) ? id.GetRawText().Trim('"') : Guid.NewGuid().ToString(),
                        Name = doc.TryGetProperty("name", out var nm) ? nm.GetString() ?? columnName : columnName,
                        Position = 0
                    };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("CreateColumnAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to create column: {resp.StatusCode} - {err}");
            }
        }

        public async Task<IReadOnlyList<ProjectColumnDto>> ListColumnsAsync(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/{Uri.EscapeDataString(projectId)}/columns";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                    var list = new List<ProjectColumnDto>();
                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        int pos = 0;
                        foreach (var it in arr.EnumerateArray())
                        {
                            var id = it.TryGetProperty("id", out var idp) ? idp.GetRawText().Trim('"') : Guid.NewGuid().ToString();
                            var nm = it.TryGetProperty("name", out var nmp) ? nmp.GetString() ?? string.Empty : string.Empty;
                            list.Add(new ProjectColumnDto { ColumnId = id, Name = nm, Position = pos++ });
                        }
                    }
                    return list;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("ListColumnsAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to list columns: {resp.StatusCode} - {err}");
            }
        }

        public async Task<OperationResult> RenameColumnAsync(string projectId, string columnId, string newName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            if (string.IsNullOrWhiteSpace(columnId)) throw new ArgumentNullException(nameof(columnId));
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentNullException(nameof(newName));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/columns/{Uri.EscapeDataString(columnId)}";
            var payload = new { name = newName };
            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    return new OperationResult { Success = true, Message = "Column renamed" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("RenameColumnAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to rename column: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        public async Task<OperationResult> DeleteColumnAsync(string projectId, string columnId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            if (string.IsNullOrWhiteSpace(columnId)) throw new ArgumentNullException(nameof(columnId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/columns/{Uri.EscapeDataString(columnId)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = "Column deleted" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Column not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("DeleteColumnAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to delete column: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        #endregion

        #region Cards (classic project cards)

        public async Task<ProjectCardDto> CreateCardAsync(ProjectCardDto createRequest, string? idempotencyKey = null, CancellationToken ct = default)
        {
            if (createRequest == null) throw new ArgumentNullException(nameof(createRequest));
            if (string.IsNullOrWhiteSpace(createRequest.ProjectId)) throw new ArgumentException("ProjectId required", nameof(createRequest));
            if (string.IsNullOrWhiteSpace(createRequest.ColumnId)) throw new ArgumentException("ColumnId required", nameof(createRequest));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                client.DefaultRequestHeaders.Remove("Idempotency-Key");
                client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
            }

            // Classic project cards support either content_id/content_type (link to issue/pr) or note
            var url = $"https://api.github.com/projects/columns/{Uri.EscapeDataString(createRequest.ColumnId)}/cards";
            object payload;
            if (createRequest.CardType == ProjectCardType.Issue && !string.IsNullOrWhiteSpace(createRequest.LinkedRepo) && createRequest.LinkedIssueNumber.HasValue)
            {
                // Link to existing issue: need content_id (issue id) and content_type "Issue"
                // We must resolve issue id from owner/repo/number
                var issueId = await ResolveIssueIdAsync(createRequest.LinkedRepo, createRequest.LinkedIssueNumber.Value, ct).ConfigureAwait(false);
                payload = new { content_id = issueId, content_type = "Issue" };
            }
            else
            {
                // Note card
                payload = new { note = createRequest.Body ?? createRequest.Title ?? string.Empty };
            }

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);

                    // Map to ProjectCardDto. Classic API returns id, note, content_url (if linked)
                    var cardId = doc.TryGetProperty("id", out var idp) ? idp.GetRawText().Trim('"') : Guid.NewGuid().ToString();
                    var note = doc.TryGetProperty("note", out var np) ? np.GetString() : null;
                    var contentUrl = doc.TryGetProperty("content_url", out var cup) ? cup.GetString() : null;

                    var dto = new ProjectCardDto
                    {
                        CardId = cardId,
                        ProjectId = createRequest.ProjectId,
                        ColumnId = createRequest.ColumnId,
                        CardType = string.IsNullOrWhiteSpace(contentUrl) ? ProjectCardType.Note : ProjectCardType.Issue,
                        Title = createRequest.Title ?? note ?? string.Empty,
                        Body = note,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    if (!string.IsNullOrWhiteSpace(contentUrl))
                    {
                        var parsed = ParseIssueUrl(contentUrl);
                        if (parsed.HasValue)
                        {
                            var (owner, repo, number) = parsed.Value;
                            dto.LinkedRepo = $"{owner}/{repo}";
                            dto.LinkedIssueNumber = number;
                        }
                    }


                    return dto;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("CreateCardAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to create card: {resp.StatusCode} - {err}");
            }
        }

        public async Task<ProjectCardDto?> GetCardAsync(string cardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/columns/cards/{Uri.EscapeDataString(cardId)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);

                    var note = doc.TryGetProperty("note", out var np) ? np.GetString() : null;
                    var contentUrl = doc.TryGetProperty("content_url", out var cup) ? cup.GetString() : null;
                    var columnUrl = doc.TryGetProperty("column_url", out var col) ? col.GetString() : null;
                    string columnId = ExtractColumnIdFromUrl(columnUrl);

                    var dto = new ProjectCardDto
                    {
                        CardId = cardId,
                        ProjectId = doc.TryGetProperty("project_id", out var pid) ? pid.GetRawText().Trim('"') : string.Empty,
                        ColumnId = columnId,
                        CardType = string.IsNullOrWhiteSpace(contentUrl) ? ProjectCardType.Note : ProjectCardType.Issue,
                        Title = note ?? string.Empty,
                        Body = note,
                        CreatedAt = doc.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow,
                        UpdatedAt = doc.TryGetProperty("updated_at", out var ua) && ua.ValueKind == JsonValueKind.String ? DateTime.Parse(ua.GetString()!) : DateTime.UtcNow
                    };

                    if (!string.IsNullOrWhiteSpace(contentUrl))
                    {
                        var parsed = ParseIssueUrl(contentUrl);
                        if (parsed.HasValue)
                        {
                            var (owner, repo, number) = parsed.Value;
                            dto.LinkedRepo = $"{owner}/{repo}";
                            dto.LinkedIssueNumber = number;
                        }
                    }


                    return dto;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogWarning("GetCardAsync unexpected status {Status}: {Body}", resp.StatusCode, err);
                return null;
            }
        }

        public async Task<ProjectCardDto> UpdateCardAsync(ProjectCardDto updateRequest, string? ifMatchETag = null, CancellationToken ct = default)
        {
            if (updateRequest == null) throw new ArgumentNullException(nameof(updateRequest));
            if (string.IsNullOrWhiteSpace(updateRequest.CardId)) throw new ArgumentException("CardId required", nameof(updateRequest));
            ct.ThrowIfCancellationRequested();

            // Classic project cards support updating note via PATCH /projects/columns/cards/{card_id}
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));
            if (!string.IsNullOrWhiteSpace(ifMatchETag))
            {
                client.DefaultRequestHeaders.IfMatch.Clear();
                client.DefaultRequestHeaders.IfMatch.Add(new EntityTagHeaderValue(ifMatchETag));
            }

            var url = $"https://api.github.com/projects/columns/cards/{Uri.EscapeDataString(updateRequest.CardId)}";
            var payload = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(updateRequest.Body)) payload["note"] = updateRequest.Body;
            // moving between columns is a separate API (MoveCardAsync), so we only update note here.

            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    // Return updated DTO by fetching card
                    var updated = await GetCardAsync(updateRequest.CardId, ct).ConfigureAwait(false);
                    if (updated == null) throw new InvalidOperationException("Updated card not found after update.");
                    return updated;
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("UpdateCardAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to update card: {resp.StatusCode} - {err}");
            }
        }

        public async Task<OperationResult> MoveCardAsync(string cardId, string targetColumnId, int? positionAfterIndex = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            if (string.IsNullOrWhiteSpace(targetColumnId)) throw new ArgumentNullException(nameof(targetColumnId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            var url = $"https://api.github.com/projects/columns/cards/{Uri.EscapeDataString(cardId)}/moves";
            // position can be "top", "bottom", or "after:<card_id>"
            string position = "top";
            if (positionAfterIndex.HasValue)
            {
                // Classic API expects after_id; we don't have card ordering index here, so default to bottom
                position = "bottom";
            }

            var payload = new Dictionary<string, object?>
            {
                ["position"] = position,
                ["column_id"] = targetColumnId
            };

            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.Created || resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return new OperationResult { Success = true, Message = "Card moved" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("MoveCardAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to move card: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        public async Task<OperationResult> DeleteCardAsync(string cardId, bool archiveOnly = true, bool deleteLinkedIssue = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            ct.ThrowIfCancellationRequested();

            // Classic API: DELETE /projects/columns/cards/{card_id}
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            // If archiveOnly is true, we can update the card's note to indicate archived and/or move to an "Archive" column.
            // Classic API does not have an "archive" flag for cards; deletion is the only way to remove. We'll treat archiveOnly=false as delete.
            if (archiveOnly)
            {
                // Best-effort: update note to indicate archived
                try
                {
                    var existing = await GetCardAsync(cardId, ct).ConfigureAwait(false);
                    if (existing == null) return new OperationResult { Success = false, Message = "Card not found", ErrorCode = "NotFound" };

                    var archivedNote = $"[ARCHIVED by ProjectsPlus at {DateTime.UtcNow:O}]\n{existing.Body}";
                    var update = new ProjectCardDto
                    {
                        CardId = existing.CardId,
                        ProjectId = existing.ProjectId,
                        ColumnId = existing.ColumnId,
                        CardType = existing.CardType,
                        Title = existing.Title,
                        Body = archivedNote,
                        CreatedAt = existing.CreatedAt,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await UpdateCardAsync(update, null, ct).ConfigureAwait(false);
                    return new OperationResult { Success = true, Message = "Card archived (note updated)" };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Archive-only fallback failed for card {CardId}", cardId);
                    return new OperationResult { Success = false, Message = "Archive attempt failed", ErrorCode = "ArchiveFailed" };
                }
            }

            // Hard delete
            var url = $"https://api.github.com/projects/columns/cards/{Uri.EscapeDataString(cardId)}";
            var req = new HttpRequestMessage(HttpMethod.Delete, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    // Optionally delete linked issue if requested (requires additional permission)
                    if (deleteLinkedIssue)
                    {
                        try
                        {
                            var card = await GetCardAsync(cardId, ct).ConfigureAwait(false);
                            if (card?.LinkedRepo != null && card.LinkedIssueNumber.HasValue)
                            {
                                var parsed = card.LinkedRepo.Split('/');
                                if (parsed.Length == 2)
                                {
                                    var delRes = await DeleteIssueIfAllowedAsync(parsed[0], parsed[1], card.LinkedIssueNumber.Value, ct).ConfigureAwait(false);
                                    if (!delRes.Success)
                                    {
                                        _logger.LogWarning("Delete linked issue failed: {Msg}", delRes.Message);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete linked issue for card {CardId}", cardId);
                        }
                    }

                    return new OperationResult { Success = true, Message = "Card deleted" };
                }

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return new OperationResult { Success = false, Message = "Card not found", ErrorCode = "NotFound" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("DeleteCardAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to delete card: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        #endregion

        #region Listing and comments

        public async Task<PagedResult<ProjectCardDto>> ListCardsAsync(string projectId, string? columnId = null, string? assignee = null, string? label = null, DateTime? updatedSince = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            ct.ThrowIfCancellationRequested();

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(ProjectsPreviewAccept));

            // Classic API lists cards per column: GET /projects/columns/{column_id}/cards
            // If columnId provided, list that column; otherwise list all columns and aggregate.
            var cards = new List<ProjectCardDto>();

            if (!string.IsNullOrWhiteSpace(columnId))
            {
                var url = $"https://api.github.com/projects/columns/{Uri.EscapeDataString(columnId)}/cards?per_page={pageSize}";
                if (!string.IsNullOrWhiteSpace(continuationToken)) url += $"&page={Uri.EscapeDataString(continuationToken)}";
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
                using (resp)
                {
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                        if (arr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in arr.EnumerateArray())
                            {
                                var id = it.TryGetProperty("id", out var idp) ? idp.GetRawText().Trim('"') : Guid.NewGuid().ToString();
                                var note = it.TryGetProperty("note", out var np) ? np.GetString() : null;
                                var contentUrl = it.TryGetProperty("content_url", out var cup) ? cup.GetString() : null;
                                var createdAt = it.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
                                var dto = new ProjectCardDto
                                {
                                    CardId = id,
                                    ProjectId = projectId,
                                    ColumnId = columnId!,
                                    CardType = string.IsNullOrWhiteSpace(contentUrl) ? ProjectCardType.Note : ProjectCardType.Issue,
                                    Title = note ?? string.Empty,
                                    Body = note,
                                    CreatedAt = createdAt,
                                    UpdatedAt = createdAt
                                };
                                if (!string.IsNullOrWhiteSpace(contentUrl))
                                {
                                    var parsed = ParseIssueUrl(contentUrl);
                                    if (parsed.HasValue)
                                    {
                                        var (owner, repo, number) = parsed.Value;
                                        dto.LinkedRepo = $"{owner}/{repo}";
                                        dto.LinkedIssueNumber = number;
                                    }
                                }

                                cards.Add(dto);
                            }
                        }

                        return new PagedResult<ProjectCardDto> { Items = cards, ContinuationToken = null, TotalCount = cards.Count };
                    }

                    var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogError("ListCardsAsync failed: {Status} {Body}", resp.StatusCode, err);
                    throw new InvalidOperationException($"Failed to list cards: {resp.StatusCode} - {err}");
                }
            }
            else
            {
                // List all columns then list cards per column
                var columns = await ListColumnsAsync(projectId, ct).ConfigureAwait(false);
                foreach (var col in columns)
                {
                    var colCards = await ListCardsAsync(projectId, col.ColumnId, assignee, label, updatedSince, pageSize, null, ct).ConfigureAwait(false);
                    cards.AddRange(colCards.Items);
                }

                return new PagedResult<ProjectCardDto> { Items = cards, ContinuationToken = null, TotalCount = cards.Count };
            }
        }

        public async Task<OperationResult> AddCommentToCardAsync(string cardId, string commentBody, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            if (string.IsNullOrWhiteSpace(commentBody)) throw new ArgumentNullException(nameof(commentBody));
            ct.ThrowIfCancellationRequested();

            // For linked issues, map to issue comment; for note cards, store comment as note update (append)
            var card = await GetCardAsync(cardId, ct).ConfigureAwait(false);
            if (card == null) return new OperationResult { Success = false, Message = "Card not found", ErrorCode = "NotFound" };

            if (card.CardType == ProjectCardType.Issue && !string.IsNullOrWhiteSpace(card.LinkedRepo) && card.LinkedIssueNumber.HasValue)
            {
                var parts = card.LinkedRepo.Split('/');
                if (parts.Length != 2) return new OperationResult { Success = false, Message = "Invalid linked repo", ErrorCode = "InvalidLinkedRepo" };

                var owner = parts[0];
                var repo = parts[1];
                var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{card.LinkedIssueNumber.Value}/comments";

                var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
                using var client = CreateAuthClient(token);
                var payload = new { body = commentBody };
                var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
                };

                var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
                using (resp)
                {
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                    {
                        return new OperationResult { Success = true, Message = "Comment added to issue" };
                    }

                    var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogError("AddCommentToCardAsync (issue) failed: {Status} {Body}", resp.StatusCode, err);
                    return new OperationResult { Success = false, Message = $"Failed to add comment: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
                }
            }
            else
            {
                // Note card: append comment to note text
                var existing = await GetCardAsync(cardId, ct).ConfigureAwait(false);
                if (existing == null) return new OperationResult { Success = false, Message = "Card not found", ErrorCode = "NotFound" };

                var appended = $"{existing.Body}\n\n[Comment by ProjectsPlus at {DateTime.UtcNow:O}]\n{commentBody}";
                var update = new ProjectCardDto
                {
                    CardId = existing.CardId,
                    ProjectId = existing.ProjectId,
                    ColumnId = existing.ColumnId,
                    CardType = existing.CardType,
                    Title = existing.Title,
                    Body = appended,
                    CreatedAt = existing.CreatedAt,
                    UpdatedAt = DateTime.UtcNow
                };

                await UpdateCardAsync(update, null, ct).ConfigureAwait(false);
                return new OperationResult { Success = true, Message = "Comment appended to note card" };
            }
        }

        public async Task<IReadOnlyList<(string Author, string Body, DateTime CreatedAt)>> GetCardCommentsAsync(string cardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            ct.ThrowIfCancellationRequested();

            var card = await GetCardAsync(cardId, ct).ConfigureAwait(false);
            if (card == null) return Array.Empty<(string, string, DateTime)>();

            if (card.CardType == ProjectCardType.Issue && !string.IsNullOrWhiteSpace(card.LinkedRepo) && card.LinkedIssueNumber.HasValue)
            {
                var parts = card.LinkedRepo.Split('/');
                if (parts.Length != 2) return Array.Empty<(string, string, DateTime)>();
                var owner = parts[0];
                var repo = parts[1];
                var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{card.LinkedIssueNumber.Value}/comments";

                var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
                using var client = CreateAuthClient(token);
                var req = new HttpRequestMessage(HttpMethod.Get, url);

                var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
                using (resp)
                {
                    if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                        var list = new List<(string, string, DateTime)>();
                        if (arr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in arr.EnumerateArray())
                            {
                                var author = it.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                                var b = it.TryGetProperty("body", out var bp) ? bp.GetString() ?? string.Empty : string.Empty;
                                var created = it.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
                                list.Add((author, b, created));
                            }
                        }
                        return list;
                    }

                    var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                    _logger.LogWarning("GetCardCommentsAsync failed: {Status} {Body}", resp.StatusCode, err);
                    return Array.Empty<(string, string, DateTime)>();
                }
            }
            else
            {
                // Note card: no separate comments; return the note as a single "comment"
                return new[] { ("system", card.Body ?? string.Empty, card.UpdatedAt) };
            }
        }

        #endregion

        #region Reconciliation and helpers

        public async Task<ProjectCardDto> ReconcileCardAsync(string cardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) throw new ArgumentNullException(nameof(cardId));
            ct.ThrowIfCancellationRequested();

            // For classic cards, fetch the card and if linked to an issue, refresh issue state and update card note/title if needed.
            var card = await GetCardAsync(cardId, ct).ConfigureAwait(false);
            if (card == null) throw new NotFoundException($"Card {cardId} not found.");

            if (card.CardType == ProjectCardType.Issue && !string.IsNullOrWhiteSpace(card.LinkedRepo) && card.LinkedIssueNumber.HasValue)
            {
                var parts = card.LinkedRepo.Split('/');
                if (parts.Length == 2)
                {
                    var owner = parts[0];
                    var repo = parts[1];
                    var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{card.LinkedIssueNumber.Value}";
                    var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
                    using var client = CreateAuthClient(token);
                    var req = new HttpRequestMessage(HttpMethod.Get, url);

                    var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
                    using (resp)
                    {
                        if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                        {
                            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                            var title = doc.TryGetProperty("title", out var t) ? t.GetString() ?? card.Title : card.Title;
                            var bodyText = doc.TryGetProperty("body", out var b) ? b.GetString() : card.Body;
                            var updatedAt = doc.TryGetProperty("updated_at", out var ua) && ua.ValueKind == JsonValueKind.String ? DateTime.Parse(ua.GetString()!) : DateTime.UtcNow;

                            var updated = new ProjectCardDto
                            {
                                CardId = card.CardId,
                                ProjectId = card.ProjectId,
                                ColumnId = card.ColumnId,
                                CardType = card.CardType,
                                Title = title,
                                Body = bodyText,
                                LinkedRepo = card.LinkedRepo,
                                LinkedIssueNumber = card.LinkedIssueNumber,
                                CreatedAt = card.CreatedAt,
                                UpdatedAt = updatedAt
                            };

                            // Update note to reflect latest issue body if desired (best-effort)
                            try
                            {
                                await UpdateCardAsync(updated, null, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to update card note during reconciliation for {CardId}", cardId);
                            }

                            return updated;
                        }

                        var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                        _logger.LogWarning("ReconcileCardAsync failed to fetch issue: {Status} {Body}", resp.StatusCode, err);
                    }
                }
            }

            // For note cards or if linked issue not found, return the card as-is
            return card;
        }

        #endregion

        #region Internal helpers for projects/cards

        private static string ExtractColumnIdFromUrl(string? columnUrl)
        {
            if (string.IsNullOrWhiteSpace(columnUrl)) return string.Empty;
            // columnUrl like https://api.github.com/projects/columns/{column_id}
            var parts = columnUrl.Split('/');
            return parts.LastOrDefault() ?? string.Empty;
        }

        private static (string Owner, string Repo, int Number)? ParseIssueUrl(string contentUrl)
        {
            if (string.IsNullOrWhiteSpace(contentUrl)) return null;
            // Expect: https://api.github.com/repos/{owner}/{repo}/issues/{number}
            try
            {
                var uri = new Uri(contentUrl);
                var segs = uri.AbsolutePath.Trim('/').Split('/');
                // segs: ["repos","{owner}","{repo}","issues","{number}"] or ["repos","{owner}","{repo}","issues","{number}"]
                if (segs.Length >= 5)
                {
                    var owner = segs[1];
                    var repo = segs[2];
                    if (int.TryParse(segs[4], out var num))
                    {
                        return (owner, repo, num);
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private async Task<long> ResolveIssueIdAsync(string ownerRepo, int issueNumber, CancellationToken ct)
        {
            var parts = ownerRepo.Split('/');
            if (parts.Length != 2) throw new ArgumentException("ownerRepo must be owner/repo");
            var owner = parts[0];
            var repo = parts[1];

            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{issueNumber}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsProjects);
                    if (doc.TryGetProperty("id", out var idp) && idp.TryGetInt64(out var idVal)) return idVal;
                    throw new InvalidOperationException("Issue id not found in response.");
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogError("ResolveIssueIdAsync failed: {Status} {Body}", resp.StatusCode, err);
                throw new InvalidOperationException($"Failed to resolve issue id: {resp.StatusCode} - {err}");
            }
        }

        private async Task<OperationResult> DeleteIssueIfAllowedAsync(string owner, string repo, int issueNumber, CancellationToken ct)
        {
            // GitHub does not support deleting issues via API; you can close them. We'll close the issue instead.
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{issueNumber}";
            var payload = new { state = "closed" };
            var req = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload, _jsonOptsProjects), Encoding.UTF8, "application/json")
            };

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    return new OperationResult { Success = true, Message = "Issue closed (delete not supported by GitHub API)" };
                }

                var err = await SafeReadStringAsync(resp).ConfigureAwait(false);
                _logger.LogWarning("DeleteIssueIfAllowedAsync failed: {Status} {Body}", resp.StatusCode, err);
                return new OperationResult { Success = false, Message = $"Failed to close issue: {resp.StatusCode}", ErrorCode = resp.StatusCode.ToString() };
            }
        }

        private ProjectDto MapProjectFromJson(JsonElement doc, ProjectType type, string? repositoryFullName)
        {
            var id = doc.TryGetProperty("id", out var idp) ? idp.GetRawText().Trim('"') : Guid.NewGuid().ToString();
            var name = doc.TryGetProperty("name", out var np) ? np.GetString() ?? string.Empty : string.Empty;
            var created = doc.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
            var columns = Array.Empty<ProjectColumnDto>();
            return new ProjectDto
            {
                ProjectId = id,
                Name = name,
                Type = type,
                RepositoryFullName = repositoryFullName,
                Columns = columns,
                CreatedAt = created
            };
        }

        #endregion
    }
}
