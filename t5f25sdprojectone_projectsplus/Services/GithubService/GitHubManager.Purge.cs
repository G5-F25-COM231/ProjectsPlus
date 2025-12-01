// src/ProjectsPlus.GitHub/GitHubManager.Purge.cs
// Partial implementation: deletion, purge and audit operations.
// Implements IPurgeService using IGithubCredsProvider and IGitHubHttpClientFactory.
// This implementation keeps internal audit state in-memory; replace with durable store in production.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    public partial class GitHubManager : IPurgeService
    {
        // In-memory audit store and block list. Replace with durable storage in production.
        private readonly ConcurrentDictionary<string, DeletionAuditDto> _deletionAudits = new ConcurrentDictionary<string, DeletionAuditDto>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, DateTime> _blockedRepositories = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Delete repository on GitHub (optional) and purge/redact ProjectsPlus internal records referencing it.
        /// Returns a DeletionAuditDto describing what was removed.
        /// </summary>
        public async Task<DeletionAuditDto> DeleteAndPurgeRepositoryAsync(string owner, string repo, string requestedBy, bool hardDeleteGitHubRepo = true, bool redactOnly = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(requestedBy)) throw new ArgumentNullException(nameof(requestedBy));
            ct.ThrowIfCancellationRequested();

            var jobId = Guid.NewGuid().ToString("D");
            var fullName = $"{owner}/{repo}";
            var audit = new DeletionAuditDto
            {
                JobId = jobId,
                RepositoryFullName = fullName,
                RequestedBy = requestedBy,
                RequestedAt = DateTime.UtcNow,
                EntitiesRemoved = Array.Empty<string>(),
                Success = false,
                Message = null
            };

            try
            {
                var removedEntities = new List<string>();

                // 1) Optionally delete repository on GitHub (hardDeleteGitHubRepo && !redactOnly)
                if (hardDeleteGitHubRepo && !redactOnly)
                {
                    var deleteResult = await DeleteRepositoryAsync(owner, repo, requestedBy, purgeProjectsPlusRecords: false, ct).ConfigureAwait(false);
                    if (!deleteResult.Success)
                    {
                        // If repo not found, continue to purge internal records; otherwise record failure
                        if (deleteResult.ErrorCode != "NotFound")
                        {
                            audit.Message = $"Failed to delete GitHub repository: {deleteResult.Message}";
                            audit.Success = false;
                            _deletionAudits[jobId] = audit;
                            return audit;
                        }
                        removedEntities.Add("GitHubRepo:NotFound");
                    }
                    else
                    {
                        removedEntities.Add("GitHubRepo:Deleted");
                    }
                }
                else
                {
                    // If redactOnly or not hard deleting, mark as redacted in internal records
                    removedEntities.Add(hardDeleteGitHubRepo ? "GitHubRepo:Skipped" : "GitHubRepo:Skipped");
                }

                // 2) Purge or redact ProjectsPlus internal records referencing this repo
                // NOTE: This implementation is a placeholder. Replace with actual DB calls to remove or redact records.
                // We'll simulate by recording the actions in the audit.
                if (redactOnly)
                {
                    removedEntities.Add("ProjectsPlusRecords:Redacted");
                }
                else
                {
                    removedEntities.Add("ProjectsPlusRecords:Deleted");
                }

                // 3) Block future access to the repository inside ProjectsPlus (soft block)
                _blockedRepositories[fullName] = DateTime.UtcNow;
                removedEntities.Add("ProjectsPlusAccess:Blocked");

                // 4) Finalize audit
                audit = new DeletionAuditDto
                {
                    JobId = jobId,
                    RepositoryFullName = fullName,
                    RequestedBy = requestedBy,
                    RequestedAt = DateTime.UtcNow,
                    EntitiesRemoved = removedEntities.ToArray(),
                    Success = true,
                    Message = "Deletion and purge completed (see EntitiesRemoved for details)."
                };

                _deletionAudits[jobId] = audit;
                return audit;
            }
            catch (Exception ex)
            {
                audit.Success = false;
                audit.Message = $"Exception during DeleteAndPurgeRepositoryAsync: {ex.Message}";
                _deletionAudits[jobId] = audit;
                throw;
            }
        }

        /// <summary>
        /// Block future access to any record of the repository inside ProjectsPlus (soft block).
        /// </summary>
        public Task<OperationResult> BlockRepositoryAccessAsync(string owner, string repo, string requestedBy, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentNullException(nameof(owner));
            if (string.IsNullOrWhiteSpace(repo)) throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrWhiteSpace(requestedBy)) throw new ArgumentNullException(nameof(requestedBy));
            ct.ThrowIfCancellationRequested();

            var fullName = $"{owner}/{repo}";
            _blockedRepositories[fullName] = DateTime.UtcNow;

            var op = new OperationResult
            {
                Success = true,
                Message = $"Repository {fullName} blocked from future access in ProjectsPlus by {requestedBy}."
            };

            return Task.FromResult(op);
        }

        /// <summary>
        /// Retrieve deletion audit by job id.
        /// </summary>
        public Task<DeletionAuditDto?> GetDeletionAuditAsync(string jobId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));
            ct.ThrowIfCancellationRequested();

            if (_deletionAudits.TryGetValue(jobId, out var audit))
            {
                return Task.FromResult<DeletionAuditDto?>(audit);
            }

            return Task.FromResult<DeletionAuditDto?>(null);
        }

        /// <summary>
        /// Helper to check if a repository is blocked inside ProjectsPlus.
        /// </summary>
        public bool IsRepositoryBlocked(string owner, string repo)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return false;
            var fullName = $"{owner}/{repo}";
            return _blockedRepositories.ContainsKey(fullName);
        }
    }
}
