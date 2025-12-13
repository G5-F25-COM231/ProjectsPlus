// src/ProjectsPlus.GitHub.Contracts.cs
// Namespace: t5f25sdprojectone_projectsplus.GitHub
// Purpose: DTOs, domain exceptions, and interfaces for repository lifecycle, project boards, collaborators,
//          contributions, and purge/audit operations. Implement as a single service (use partial classes).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace t5f25sdprojectone_projectsplus.Services.GithubService
{
    #region Common Types and DTOs

    public enum RepositoryVisibility { Public, Private, Internal }

    public enum CollaboratorPermission { Read, Triage, Write, Maintain, Admin }

    public enum ProjectType { Classic, ProjectsV2 }

    public enum ProjectCardType { Note, Issue }

    public sealed class OperationResult
    {
        public bool Success { get; init; }
        public string? Message { get; init; }
        public string? ErrorCode { get; init; }
    }

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public string? ContinuationToken { get; init; }
        public int TotalCount { get; init; }
    }

    public sealed class RepositoryDto
    {
        public string Owner { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string FullName => $"{Owner}/{Name}";
        public string? Description { get; init; }
        public RepositoryVisibility Visibility { get; init; }
        public string DefaultBranch { get; init; } = "main";
        public Uri? HtmlUrl { get; init; }
        public DateTime CreatedAt { get; init; }
        public long RepoId { get; init; }
    }

    public sealed class BranchProtectionDto
    {
        public string BranchName { get; init; } = "main";
        public bool RequirePullRequest { get; init; } = true;
        public int RequiredApprovingReviewCount { get; init; } = 1;
        public bool RequireStatusChecks { get; init; } = false;
        public IReadOnlyList<string>? RequiredStatusCheckContexts { get; init; }
        public bool EnforceAdmins { get; init; } = true;
        public IReadOnlyList<string>? RestrictPushTo { get; init; } // users/teams
    }

    public sealed class ProjectDto
    {
        public string ProjectId { get; init; } = string.Empty; // GraphQL id or REST id
        public string Name { get; init; } = string.Empty;
        public ProjectType Type { get; init; }
        public string? RepositoryFullName { get; init; } // optional scope
        public IReadOnlyList<ProjectColumnDto> Columns { get; init; } = Array.Empty<ProjectColumnDto>();
        public DateTime CreatedAt { get; init; }
    }

    public sealed class ProjectColumnDto
    {
        public string ColumnId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Position { get; init; }
    }

    public sealed class ProjectCardDto
    {
        public string CardId { get; init; } = string.Empty; // project item id or card id
        public string ProjectId { get; init; } = string.Empty;
        public string ColumnId { get; init; } = string.Empty;
        public ProjectCardType CardType { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Body { get; init; }
        public string? LinkedRepo { get; set; } // owner/repo
        public int? LinkedIssueNumber { get; set; }
        public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Assignees { get; init; } = Array.Empty<string>();
        public IDictionary<string, string>? CustomFields { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string? ETag { get; init; } // for optimistic concurrency
    }

    public sealed class CollaboratorDto
    {
        public string Username { get; init; } = string.Empty;
        public CollaboratorPermission Permission { get; set; }
        public bool IsPendingInvite { get; init; }
        public DateTime? InvitedAt { get; init; }
    }

    public sealed class ContributionSummaryDto
    {
        public string Username { get; init; } = string.Empty;
        public int CommitCount { get; init; }
        public int PullRequestsOpened { get; init; }
        public int PullRequestsMerged { get; init; }
        public int IssuesOpened { get; init; }
        public int Reviews { get; init; }
        public DateTime WindowStart { get; init; }
        public DateTime WindowEnd { get; init; }
    }

    public sealed class DeletionAuditDto
    {
        public string JobId { get; init; } = Guid.NewGuid().ToString();
        public string RepositoryFullName { get; init; } = string.Empty;
        public string RequestedBy { get; init; } = string.Empty;
        public DateTime RequestedAt { get; init; }
        public IReadOnlyList<string> EntitiesRemoved { get; init; } = Array.Empty<string>();
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    #endregion

    #region Domain Exceptions

    public sealed class GitHubRateLimitException : Exception { public GitHubRateLimitException(string message) : base(message) { } }
    public sealed class GitHubForbiddenException : Exception { public GitHubForbiddenException(string message) : base(message) { } }
    public sealed class NotFoundException : Exception { public NotFoundException(string message) : base(message) { } }
    public sealed class ConflictException : Exception { public ConflictException(string message) : base(message) { } }

    #endregion

    #region Repository Lifecycle and Branch Protection

    public interface IGitHubRepositoryManager
    {
        Task<RepositoryDto> CreateRepositoryAsync(RepositoryDto repoSpec, bool initializeWithReadme = true, CancellationToken ct = default);

        Task<RepositoryDto?> GetRepositoryAsync(string owner, string name, CancellationToken ct = default);

        Task<PagedResult<RepositoryDto>> ListRepositoriesAsync(string ownerOrOrg, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default);

        Task<OperationResult> DeleteRepositoryAsync(string owner, string name, string requestedBy, bool purgeProjectsPlusRecords = false, CancellationToken ct = default);

        Task<OperationResult> ProtectBranchAsync(string owner, string repo, BranchProtectionDto protection, CancellationToken ct = default);

        Task<BranchProtectionDto?> GetBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default);

        Task<OperationResult> RemoveBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default);
    }

    #endregion

    #region Project Board and Card Management

    public interface IProjectBoardService
    {
        // Project lifecycle
        Task<ProjectDto> CreateProjectAsync(string name, ProjectType type, string? repositoryFullName = null, CancellationToken ct = default);
        Task<ProjectDto?> GetProjectAsync(string projectId, CancellationToken ct = default);
        Task<OperationResult> DeleteProjectAsync(string projectId, CancellationToken ct = default);

        // Columns
        Task<ProjectColumnDto> CreateColumnAsync(string projectId, string columnName, CancellationToken ct = default);
        Task<IReadOnlyList<ProjectColumnDto>> ListColumnsAsync(string projectId, CancellationToken ct = default);
        Task<OperationResult> RenameColumnAsync(string projectId, string columnId, string newName, CancellationToken ct = default);
        Task<OperationResult> DeleteColumnAsync(string projectId, string columnId, CancellationToken ct = default);

        // Cards (issues / notes)
        Task<ProjectCardDto> CreateCardAsync(ProjectCardDto createRequest, string? idempotencyKey = null, CancellationToken ct = default);
        Task<ProjectCardDto?> GetCardAsync(string cardId, CancellationToken ct = default);
        Task<ProjectCardDto> UpdateCardAsync(ProjectCardDto updateRequest, string? ifMatchETag = null, CancellationToken ct = default);
        Task<OperationResult> MoveCardAsync(string cardId, string targetColumnId, int? positionAfterIndex = null, CancellationToken ct = default);
        Task<OperationResult> DeleteCardAsync(string cardId, bool archiveOnly = true, bool deleteLinkedIssue = false, CancellationToken ct = default);

        // Listing and querying
        Task<PagedResult<ProjectCardDto>> ListCardsAsync(string projectId, string? columnId = null, string? assignee = null, string? label = null, DateTime? updatedSince = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);

        // Comments and activity
        Task<OperationResult> AddCommentToCardAsync(string cardId, string commentBody, CancellationToken ct = default);
        Task<IReadOnlyList<(string Author, string Body, DateTime CreatedAt)>> GetCardCommentsAsync(string cardId, CancellationToken ct = default);

        // Reconciliation
        Task<ProjectCardDto> ReconcileCardAsync(string cardId, CancellationToken ct = default);
    }

    #endregion

    #region Collaborators and Contributions

    public interface ICollaboratorService
    {
        Task<OperationResult> InviteCollaboratorAsync(string owner, string repo, string usernameOrEmail, CollaboratorPermission permission, TimeSpan? inviteExpiry = null, CancellationToken ct = default);

        Task<OperationResult> RemoveCollaboratorAsync(string owner, string repo, string username, CancellationToken ct = default);

        Task<IReadOnlyList<CollaboratorDto>> ListCollaboratorsAsync(string owner, string repo, CancellationToken ct = default);

        Task<OperationResult> AddTeamToRepoAsync(string org, string teamSlug, string repo, CollaboratorPermission permission, CancellationToken ct = default);
    }

    public interface IContributionService
    {
        Task<ContributionSummaryDto> GetContributionSummaryAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);

        Task<PagedResult<object>> ListContributionsAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default);

        Task<IReadOnlyList<ContributionSummaryDto>> GetProjectContributionsAsync(string projectId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);
    }

    #endregion

    #region Deletion, Purge and Audit

    public interface IPurgeService
    {
        Task<DeletionAuditDto> DeleteAndPurgeRepositoryAsync(string owner, string repo, string requestedBy, bool hardDeleteGitHubRepo = true, bool redactOnly = false, CancellationToken ct = default);

        Task<OperationResult> BlockRepositoryAccessAsync(string owner, string repo, string requestedBy, CancellationToken ct = default);

        Task<DeletionAuditDto?> GetDeletionAuditAsync(string jobId, CancellationToken ct = default);
    }

    #endregion
}
