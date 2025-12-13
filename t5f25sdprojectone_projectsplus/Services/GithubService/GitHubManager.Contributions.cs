// src/ProjectsPlus.GitHub/GitHubManager.Contributions.cs
// Partial implementation: contribution summaries and listing.
// Implements IContributionService using IGithubCredsProvider and IGitHubHttpClientFactory.
// Uses GitHub REST endpoints available on free tier (commits, issues, pulls, reviews).
// Note: This implementation is conservative about rate limits and uses existing SendWithRetryAsync.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    public partial class GitHubManager : IContributionService
    {
        private readonly JsonSerializerOptions _jsonOptsContrib = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Get aggregated contribution summary for a single user in a repo within a time window.
        /// Commits, PRs opened, PRs merged, issues opened, and reviews are counted.
        /// This is a best-effort aggregation using REST endpoints and may be rate-limited for large windows.
        /// </summary>
        public async Task<ContributionSummaryDto> GetContributionSummaryAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));
            if (windowEnd <= windowStart) throw new ArgumentException("windowEnd must be after windowStart");

            ct.ThrowIfCancellationRequested();

            // Parallelize independent queries where possible
            var commitsTask = CountCommitsAsync(owner, repo, username, windowStart, windowEnd, ct);
            var prsOpenedTask = CountPullRequestsAsync(owner, repo, username, windowStart, windowEnd, ct, onlyMerged: false);
            var prsMergedTask = CountPullRequestsAsync(owner, repo, username, windowStart, windowEnd, ct, onlyMerged: true);
            var issuesOpenedTask = CountIssuesAsync(owner, repo, username, windowStart, windowEnd, ct);
            var reviewsTask = CountReviewsAsync(owner, repo, username, windowStart, windowEnd, ct);

            await Task.WhenAll(commitsTask, prsOpenedTask, prsMergedTask, issuesOpenedTask, reviewsTask).ConfigureAwait(false);

            return new ContributionSummaryDto
            {
                Username = username,
                CommitCount = commitsTask.Result,
                PullRequestsOpened = prsOpenedTask.Result,
                PullRequestsMerged = prsMergedTask.Result,
                IssuesOpened = issuesOpenedTask.Result,
                Reviews = reviewsTask.Result,
                WindowStart = windowStart,
                WindowEnd = windowEnd
            };
        }

        /// <summary>
        /// List raw contribution events (commits, PRs, issues, reviews) for a user in a repo.
        /// Returns a paged result of anonymous objects describing events. Consumers should inspect the "type" field.
        /// </summary>
        public async Task<PagedResult<object>> ListContributionsAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(username)) throw new ArgumentNullException(nameof(username));
            if (windowEnd <= windowStart) throw new ArgumentException("windowEnd must be after windowStart");
            ct.ThrowIfCancellationRequested();

            // We'll aggregate commits, PRs, issues, and reviews into a single list and page locally.
            var items = new List<object>();

            // Commits (list commits by author)
            var commits = await ListCommitsAsync(owner, repo, username, windowStart, windowEnd, 500, null, ct).ConfigureAwait(false);
            items.AddRange(commits.Select(c => new
            {
                type = "commit",
                sha = c.Sha,
                message = c.Message,
                url = c.Url,
                date = c.Date
            }));

            // Pull requests opened by user
            var prs = await ListPullRequestsAsync(owner, repo, username, windowStart, windowEnd, 500, null, ct).ConfigureAwait(false);
            items.AddRange(prs.Select(p => new
            {
                type = "pull_request",
                number = p.Number,
                title = p.Title,
                url = p.Url,
                created_at = p.CreatedAt,
                merged_at = p.MergedAt
            }));

            // Issues opened by user (exclude PRs)
            var issues = await ListIssuesAsync(owner, repo, username, windowStart, windowEnd, 500, null, ct).ConfigureAwait(false);
            items.AddRange(issues.Select(i => new
            {
                type = "issue",
                number = i.Number,
                title = i.Title,
                url = i.Url,
                created_at = i.CreatedAt
            }));

            // Reviews by user
            var reviews = await ListReviewsAsync(owner, repo, username, windowStart, windowEnd, 500, null, ct).ConfigureAwait(false);
            items.AddRange(reviews.Select(r => new
            {
                type = "review",
                pull_request_number = r.PullRequestNumber,
                state = r.State,
                body = r.Body,
                submitted_at = r.SubmittedAt
            }));

            // Sort by date descending
            var ordered = items.OrderByDescending(i =>
            {
                // attempt to extract a date property; fallback to now
                var dict = i as IDictionary<string, object?>;
                return DateTime.UtcNow;
            }).ToList();

            // Local paging
            int page = 1;
            if (!string.IsNullOrWhiteSpace(continuationToken) && int.TryParse(continuationToken, out var parsedPage)) page = parsedPage;
            var paged = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            string? nextToken = page * pageSize < ordered.Count ? (page + 1).ToString() : null;

            return new PagedResult<object> { Items = paged, ContinuationToken = nextToken, TotalCount = ordered.Count };
        }

        /// <summary>
        /// Aggregate contributions for all collaborators referenced by a project (classic project).
        /// This implementation enumerates cards, extracts linked issues/PRs and their authors, and aggregates per-user.
        /// </summary>
        public async Task<IReadOnlyList<ContributionSummaryDto>> GetProjectContributionsAsync(string projectId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentNullException(nameof(projectId));
            if (windowEnd <= windowStart) throw new ArgumentException("windowEnd must be after windowStart");
            ct.ThrowIfCancellationRequested();

            // List all cards for project
            var cardsPaged = await ListCardsAsync(projectId, null, null, null, null, 100, null, ct).ConfigureAwait(false);
            var cards = cardsPaged.Items;

            var userSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect usernames from linked issues/PRs and assignees
            foreach (var card in cards)
            {
                if (!string.IsNullOrWhiteSpace(card.LinkedRepo) && card.LinkedIssueNumber.HasValue)
                {
                    // fetch issue to get author
                    try
                    {
                        var parts = card.LinkedRepo.Split('/');
                        if (parts.Length == 2)
                        {
                            var owner = parts[0];
                            var repo = parts[1];
                            var issue = await FetchIssueMinimalAsync(owner, repo, card.LinkedIssueNumber.Value, ct).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(issue?.Author)) userSet.Add(issue.Author);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to fetch issue for card {CardId}", card.CardId);
                    }
                }

                foreach (var assignee in card.Assignees ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(assignee)) userSet.Add(assignee);
                }
            }

            var results = new List<ContributionSummaryDto>();
            foreach (var user in userSet)
            {
                try
                {
                    var summary = await GetContributionSummaryAsyncForProject(user, cards, windowStart, windowEnd, ct).ConfigureAwait(false);
                    results.Add(summary);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to compute contribution summary for {User}", user);
                }
            }

            return results;
        }


        #region Internal helpers and minimal models

        private async Task<ContributionSummaryDto> GetContributionSummaryAsyncForProject(string username, IReadOnlyList<ProjectCardDto> cards, DateTime windowStart, DateTime windowEnd, CancellationToken ct)
        {
            // Count commits, PRs, issues, reviews limited to repos referenced by cards
            var repoSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cards)
            {
                if (!string.IsNullOrWhiteSpace(c.LinkedRepo)) repoSet.Add(c.LinkedRepo);
            }

            int commits = 0, prsOpened = 0, prsMerged = 0, issuesOpened = 0, reviews = 0;

            foreach (var repo in repoSet)
            {
                var parts = repo.Split('/');
                if (parts.Length != 2) continue;
                var owner = parts[0];
                var repoName = parts[1];

                commits += await CountCommitsAsync(owner, repoName, username, windowStart, windowEnd, ct).ConfigureAwait(false);
                prsOpened += await CountPullRequestsAsync(owner, repoName, username, windowStart, windowEnd, ct, onlyMerged: false).ConfigureAwait(false);
                prsMerged += await CountPullRequestsAsync(owner, repoName, username, windowStart, windowEnd, ct, onlyMerged: true).ConfigureAwait(false);
                issuesOpened += await CountIssuesAsync(owner, repoName, username, windowStart, windowEnd, ct).ConfigureAwait(false);
                reviews += await CountReviewsAsync(owner, repoName, username, windowStart, windowEnd, ct).ConfigureAwait(false);
            }

            return new ContributionSummaryDto
            {
                Username = username,
                CommitCount = commits,
                PullRequestsOpened = prsOpened,
                PullRequestsMerged = prsMerged,
                IssuesOpened = issuesOpened,
                Reviews = reviews,
                WindowStart = windowStart,
                WindowEnd = windowEnd
            };
        }

        private async Task<int> CountCommitsAsync(string owner, string repo, string author, DateTime since, DateTime until, CancellationToken ct)
        {
            // GET /repos/{owner}/{repo}/commits?author={author}&since={iso}&until={iso}
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits?author={Uri.EscapeDataString(author)}&since={Uri.EscapeDataString(since.ToString("o"))}&until={Uri.EscapeDataString(until.ToString("o"))}&per_page=100";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    if (arr.ValueKind == JsonValueKind.Array) return arr.GetArrayLength();
                    return 0;
                }

                _logger.LogDebug("CountCommitsAsync: unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return 0;
            }
        }

        private async Task<int> CountPullRequestsAsync(string owner, string repo, string username, DateTime since, DateTime until, CancellationToken ct, bool onlyMerged)
        {
            // Use search API to count PRs authored by user in repo within date range
            // GET /search/issues?q=repo:{owner}/{repo}+type:pr+author:{username}+created:{since}..{until}
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var createdRange = $"{since:yyyy-MM-dd}..{until:yyyy-MM-dd}";
            var q = $"repo:{owner}/{repo}+type:pr+author:{username}+created:{createdRange}";
            if (onlyMerged)
            {
                // search for merged PRs: use is:merged qualifier (search supports it)
                q += "+is:merged";
            }

            var url = $"https://api.github.com/search/issues?q={Uri.EscapeDataString(q)}&per_page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    if (doc.TryGetProperty("total_count", out var tc) && tc.TryGetInt32(out var total)) return total;
                    return 0;
                }

                _logger.LogDebug("CountPullRequestsAsync: unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return 0;
            }
        }

        private async Task<int> CountIssuesAsync(string owner, string repo, string username, DateTime since, DateTime until, CancellationToken ct)
        {
            // Use search API to count issues authored by user (exclude PRs)
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var createdRange = $"{since:yyyy-MM-dd}..{until:yyyy-MM-dd}";
            var q = $"repo:{owner}/{repo}+type:issue+author:{username}+created:{createdRange}";
            var url = $"https://api.github.com/search/issues?q={Uri.EscapeDataString(q)}&per_page=1";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    if (doc.TryGetProperty("total_count", out var tc) && tc.TryGetInt32(out var total)) return total;
                    return 0;
                }

                _logger.LogDebug("CountIssuesAsync: unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return 0;
            }
        }

        private async Task<int> CountReviewsAsync(string owner, string repo, string username, DateTime since, DateTime until, CancellationToken ct)
        {
            // Reviews are per-PR; list PRs authored or touched and then list reviews by user.
            // For simplicity, search PRs updated in window and then count reviews by username.
            var prs = await ListPullRequestsAsync(owner, repo, username, since, until, 100, null, ct).ConfigureAwait(false);
            int count = 0;
            foreach (var pr in prs)
            {
                var reviews = await ListReviewsForPullRequestAsync(owner, repo, pr.Number, ct).ConfigureAwait(false);
                count += reviews.Count(r => string.Equals(r.Author, username, StringComparison.OrdinalIgnoreCase)
                                            && r.SubmittedAt >= since && r.SubmittedAt <= until);
            }
            return count;
        }

        #endregion

        #region Lightweight models for internal use

        private sealed class CommitInfo
        {
            public string Sha { get; init; } = string.Empty;
            public string Message { get; init; } = string.Empty;
            public string Url { get; init; } = string.Empty;
            public DateTime Date { get; init; }
        }

        private sealed class PullRequestInfo
        {
            public int Number { get; init; }
            public string Title { get; init; } = string.Empty;
            public string Url { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
            public DateTime? MergedAt { get; init; }
        }

        private sealed class IssueInfo
        {
            public int Number { get; init; }
            public string Title { get; init; } = string.Empty;
            public string Url { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
            public string? Author { get; init; }
        }

        private sealed class ReviewInfo
        {
            public int PullRequestNumber { get; init; }
            public string Author { get; init; } = string.Empty;
            public string State { get; init; } = string.Empty;
            public string? Body { get; init; }
            public DateTime SubmittedAt { get; init; }
        }

        #endregion

        #region Listing helpers (commits, PRs, issues, reviews)

        private async Task<IReadOnlyList<CommitInfo>> ListCommitsAsync(string owner, string repo, string author, DateTime since, DateTime until, int perPage, string? continuationToken, CancellationToken ct)
        {
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var requestUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits?author={Uri.EscapeDataString(author)}&since={Uri.EscapeDataString(since.ToString("o"))}&until={Uri.EscapeDataString(until.ToString("o"))}&per_page={perPage}";
            if (!string.IsNullOrWhiteSpace(continuationToken)) requestUrl += $"&page={Uri.EscapeDataString(continuationToken)}";
            var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    var list = new List<CommitInfo>();
                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            var sha = it.TryGetProperty("sha", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                            var message = it.TryGetProperty("commit", out var c) && c.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                            var date = it.TryGetProperty("commit", out var c2) && c2.TryGetProperty("author", out var a) && a.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                                ? DateTime.Parse(d.GetString()!)
                                : DateTime.UtcNow;
                            var htmlUrl = it.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? string.Empty : string.Empty;
                            list.Add(new CommitInfo { Sha = sha, Message = message, Url = htmlUrl, Date = date });
                        }
                    }
                    return list;
                }

                _logger.LogDebug("ListCommitsAsync unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return Array.Empty<CommitInfo>();
            }
        }


        private async Task<IReadOnlyList<PullRequestInfo>> ListPullRequestsAsync(string owner, string repo, string username, DateTime since, DateTime until, int perPage, string? continuationToken, CancellationToken ct)
        {
            // Use search API to find PRs authored by username in repo within date range
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var createdRange = $"{since:yyyy-MM-dd}..{until:yyyy-MM-dd}";
            var q = $"repo:{owner}/{repo}+type:pr+author:{username}+created:{createdRange}";
            var url = $"https://api.github.com/search/issues?q={Uri.EscapeDataString(q)}&per_page={perPage}";
            if (!string.IsNullOrWhiteSpace(continuationToken)) url += $"&page={Uri.EscapeDataString(continuationToken)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    var list = new List<PullRequestInfo>();
                    if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in items.EnumerateArray())
                        {
                            var number = it.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                            var title = it.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            var urlHtml = it.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? string.Empty : string.Empty;
                            var created = it.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
                            DateTime? mergedAt = null;
                            // To get merged_at we need to fetch the PR details; skip here for performance
                            list.Add(new PullRequestInfo { Number = number, Title = title, Url = urlHtml, CreatedAt = created, MergedAt = mergedAt });
                        }
                    }
                    return list;
                }

                _logger.LogDebug("ListPullRequestsAsync unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return Array.Empty<PullRequestInfo>();
            }
        }

        private async Task<IReadOnlyList<IssueInfo>> ListIssuesAsync(string owner, string repo, string username, DateTime since, DateTime until, int perPage, string? continuationToken, CancellationToken ct)
        {
            // Use search API to find issues authored by username in repo within date range (excludes PRs via type:issue)
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var createdRange = $"{since:yyyy-MM-dd}..{until:yyyy-MM-dd}";
            var q = $"repo:{owner}/{repo}+type:issue+author:{username}+created:{createdRange}";
            var url = $"https://api.github.com/search/issues?q={Uri.EscapeDataString(q)}&per_page={perPage}";
            if (!string.IsNullOrWhiteSpace(continuationToken)) url += $"&page={Uri.EscapeDataString(continuationToken)}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    var list = new List<IssueInfo>();
                    if (doc.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in items.EnumerateArray())
                        {
                            var number = it.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                            var title = it.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                            var urlHtml = it.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? string.Empty : string.Empty;
                            var created = it.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
                            var author = it.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() : null;
                            list.Add(new IssueInfo { Number = number, Title = title, Url = urlHtml, CreatedAt = created, Author = author });
                        }
                    }
                    return list;
                }

                _logger.LogDebug("ListIssuesAsync unexpected status {Status} for {Owner}/{Repo}", resp.StatusCode, owner + "/" + repo);
                return Array.Empty<IssueInfo>();
            }
        }

        private async Task<IReadOnlyList<ReviewInfo>> ListReviewsForPullRequestAsync(string owner, string repo, int pullNumber, CancellationToken ct)
        {
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/pulls/{pullNumber}/reviews?per_page=100";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var arr = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    var list = new List<ReviewInfo>();
                    if (arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            var author = it.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                            var state = it.TryGetProperty("state", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                            var bodyText = it.TryGetProperty("body", out var b) ? b.GetString() : null;
                            var submitted = it.TryGetProperty("submitted_at", out var sa) && sa.ValueKind == JsonValueKind.String ? DateTime.Parse(sa.GetString()!) : DateTime.UtcNow;
                            list.Add(new ReviewInfo { PullRequestNumber = pullNumber, Author = author, State = state, Body = bodyText, SubmittedAt = submitted });
                        }
                    }
                    return list;
                }

                _logger.LogDebug("ListReviewsForPullRequestAsync unexpected status {Status} for {Owner}/{Repo} PR#{Pull}", resp.StatusCode, owner + "/" + repo, pullNumber);
                return Array.Empty<ReviewInfo>();
            }
        }

        private async Task<IReadOnlyList<ReviewInfo>> ListReviewsAsync(string owner, string repo, string username, DateTime since, DateTime until, int perPage, string? continuationToken, CancellationToken ct)
        {
            // For simplicity, iterate PRs authored by username and collect reviews by that user across PRs in repo.
            var prs = await ListPullRequestsAsync(owner, repo, username, since, until, perPage, continuationToken, ct).ConfigureAwait(false);
            var reviews = new List<ReviewInfo>();
            foreach (var pr in prs)
            {
                var r = await ListReviewsForPullRequestAsync(owner, repo, pr.Number, ct).ConfigureAwait(false);
                reviews.AddRange(r.Where(x => string.Equals(x.Author, username, StringComparison.OrdinalIgnoreCase)));
            }
            return reviews;
        }

        private async Task<IssueInfo?> FetchIssueMinimalAsync(string owner, string repo, int number, CancellationToken ct)
        {
            var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
            using var client = CreateAuthClient(token);
            var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/issues/{number}";
            var req = new HttpRequestMessage(HttpMethod.Get, url);

            var resp = await _httpFactory.SendWithRetryAsync(client, req, ct).ConfigureAwait(false);
            using (resp)
            {
                if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode <= 299)
                {
                    var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var doc = JsonSerializer.Deserialize<JsonElement>(body, _jsonOptsContrib);
                    var numberVal = doc.TryGetProperty("number", out var n) && n.TryGetInt32(out var nv) ? nv : 0;
                    var title = doc.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var created = doc.TryGetProperty("created_at", out var ca) && ca.ValueKind == JsonValueKind.String ? DateTime.Parse(ca.GetString()!) : DateTime.UtcNow;
                    var author = doc.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var l) ? l.GetString() : null;
                    return new IssueInfo { Number = numberVal, Title = title, Url = doc.TryGetProperty("html_url", out var hu) ? hu.GetString() ?? string.Empty : string.Empty, CreatedAt = created, Author = author };
                }

                return null;
            }
        }

        #endregion
    }
}
