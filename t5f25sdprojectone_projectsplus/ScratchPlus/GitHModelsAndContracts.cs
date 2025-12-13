//// src/ProjectsPlus.GitHub/Models/ModelsAndContracts.cs
//// DTOs, enums and service contracts used across the GitHub manager partials.

//using System;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;

//namespace t5f25sdprojectone_projectsplus.ScratchPlus
//{
//    #region Credentials models

//    public sealed class GithubCredsPlus
//    {
//        public GithubPat? Pat { get; set; }
//        public GithubApp? App { get; set; }
//        public GithubWebhook? Webhook { get; set; }

//        public void ValidateForUse()
//        {
//            // Basic validation: require either PAT or App (with private key) for operations
//            if ((Pat == null || string.IsNullOrWhiteSpace(Pat.Token)) &&
//                (App == null || App.AppId <= 0 || string.IsNullOrWhiteSpace(App.PrivateKeyPem)))
//            {
//                throw new InvalidOperationException("GithubCredsPlus must contain either a PAT or a valid App configuration.");
//            }
//        }
//    }

//    public sealed class GithubPat
//    {
//        public string Token { get; set; } = string.Empty;
//        public string? Owner { get; set; }
//        public DateTime? ExpiresAt { get; set; }
//    }

//    public sealed class GithubApp
//    {
//        public long AppId { get; set; }
//        public long? InstallationId { get; set; }
//        public string PrivateKeyPem { get; set; } = string.Empty;
//    }

//    public sealed class GithubWebhook
//    {
//        public string Secret { get; set; } = string.Empty;
//    }

//    #endregion

//    #region DTOs and enums

//    public enum RepositoryVisibility
//    {
//        Public,
//        Private
//    }

//    public sealed class RepositoryDto
//    {
//        public long RepoId { get; set; }
//        public string Owner { get; set; } = string.Empty;
//        public string Name { get; set; } = string.Empty;
//        public string? Description { get; set; }
//        public RepositoryVisibility Visibility { get; set; } = RepositoryVisibility.Private;
//        public string DefaultBranch { get; set; } = "main";
//        public Uri? HtmlUrl { get; set; }
//        public DateTime CreatedAt { get; set; }
//    }

//    public sealed class BranchProtectionDto
//    {
//        public string BranchName { get; set; } = "main";
//        public bool RequireStatusChecks { get; set; }
//        public string[]? RequiredStatusCheckContexts { get; set; }
//        public bool EnforceAdmins { get; set; }
//        public bool RequirePullRequest { get; set; }
//        public int RequiredApprovingReviewCount { get; set; } = 1;
//        public List<string>? RestrictPushTo { get; set; }
//    }

//    public enum ProjectType
//    {
//        Classic,
//        V2
//    }

//    public sealed class ProjectDto
//    {
//        public string ProjectId { get; set; } = string.Empty;
//        public string Name { get; set; } = string.Empty;
//        public ProjectType Type { get; set; } = ProjectType.Classic;
//        public string? RepositoryFullName { get; set; }
//        public IReadOnlyList<ProjectColumnDto> Columns { get; set; } = Array.Empty<ProjectColumnDto>();
//        public DateTime CreatedAt { get; set; }
//    }

//    public sealed class ProjectColumnDto
//    {
//        public string ColumnId { get; set; } = string.Empty;
//        public string Name { get; set; } = string.Empty;
//        public int Position { get; set; }
//    }

//    public enum ProjectCardType
//    {
//        Note,
//        Issue
//    }

//    public sealed class ProjectCardDto
//    {
//        public string CardId { get; set; } = string.Empty;
//        public string ProjectId { get; set; } = string.Empty;
//        public string ColumnId { get; set; } = string.Empty;
//        public ProjectCardType CardType { get; set; } = ProjectCardType.Note;
//        public string Title { get; set; } = string.Empty;
//        public string? Body { get; set; }
//        public string? LinkedRepo { get; set; } // owner/repo
//        public int? LinkedIssueNumber { get; set; }
//        public IReadOnlyList<string>? Assignees { get; set; }
//        public DateTime CreatedAt { get; set; }
//        public DateTime UpdatedAt { get; set; }
//    }

//    public sealed class PagedResult<T>
//    {
//        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
//        public string? ContinuationToken { get; set; }
//        public int TotalCount { get; set; }
//    }

//    public sealed class OperationResult
//    {
//        public bool Success { get; set; }
//        public string? Message { get; set; }
//        public string? ErrorCode { get; set; }
//    }

//    public sealed class DeletionAuditDto
//    {
//        public string JobId { get; set; } = string.Empty;
//        public string RepositoryFullName { get; set; } = string.Empty;
//        public string RequestedBy { get; set; } = string.Empty;
//        public DateTime RequestedAt { get; set; }
//        public string[] EntitiesRemoved { get; set; } = Array.Empty<string>();
//        public bool Success { get; set; }
//        public string? Message { get; set; }
//    }

//    public sealed class ContributionSummaryDto
//    {
//        public string Username { get; set; } = string.Empty;
//        public int CommitCount { get; set; }
//        public int PullRequestsOpened { get; set; }
//        public int PullRequestsMerged { get; set; }
//        public int IssuesOpened { get; set; }
//        public int Reviews { get; set; }
//        public DateTime WindowStart { get; set; }
//        public DateTime WindowEnd { get; set; }
//    }

//    public enum CollaboratorPermission
//    {
//        Read,
//        Triage,
//        Write,
//        Maintain,
//        Admin
//    }

//    public sealed class CollaboratorDto
//    {
//        public string Username { get; set; } = string.Empty;
//        public CollaboratorPermission Permission { get; set; } = CollaboratorPermission.Read;
//        public bool IsPendingInvite { get; set; }
//        public DateTime? InvitedAt { get; set; }
//    }

//    #endregion

//    #region Service contracts

//    public interface IGitHubRepositoryManager
//    {
//        Task<RepositoryDto> CreateRepositoryAsync(RepositoryDto repoSpec, bool initializeWithReadme = true, CancellationToken ct = default);
//        Task<RepositoryDto?> GetRepositoryAsync(string owner, string name, CancellationToken ct = default);
//        Task<PagedResult<RepositoryDto>> ListRepositoriesAsync(string ownerOrOrg, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default);
//        Task<OperationResult> DeleteRepositoryAsync(string owner, string name, string requestedBy, bool purgeProjectsPlusRecords = false, CancellationToken ct = default);

//        Task<OperationResult> ProtectBranchAsync(string owner, string repo, BranchProtectionDto protection, CancellationToken ct = default);
//        Task<BranchProtectionDto?> GetBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default);
//        Task<OperationResult> RemoveBranchProtectionAsync(string owner, string repo, string branch = "main", CancellationToken ct = default);
//    }

//    public interface IProjectBoardService
//    {
//        Task<ProjectDto> CreateProjectAsync(string name, ProjectType type, string? repositoryFullName = null, CancellationToken ct = default);
//        Task<ProjectDto?> GetProjectAsync(string projectId, CancellationToken ct = default);
//        Task<OperationResult> DeleteProjectAsync(string projectId, CancellationToken ct = default);

//        Task<ProjectColumnDto> CreateColumnAsync(string projectId, string columnName, CancellationToken ct = default);
//        Task<IReadOnlyList<ProjectColumnDto>> ListColumnsAsync(string projectId, CancellationToken ct = default);
//        Task<OperationResult> RenameColumnAsync(string projectId, string columnId, string newName, CancellationToken ct = default);
//        Task<OperationResult> DeleteColumnAsync(string projectId, string columnId, CancellationToken ct = default);

//        Task<ProjectCardDto> CreateCardAsync(ProjectCardDto createRequest, string? idempotencyKey = null, CancellationToken ct = default);
//        Task<ProjectCardDto?> GetCardAsync(string cardId, CancellationToken ct = default);
//        Task<ProjectCardDto> UpdateCardAsync(ProjectCardDto updateRequest, string? ifMatchETag = null, CancellationToken ct = default);
//        Task<OperationResult> MoveCardAsync(string cardId, string targetColumnId, int? positionAfterIndex = null, CancellationToken ct = default);
//        Task<OperationResult> DeleteCardAsync(string cardId, bool archiveOnly = true, bool deleteLinkedIssue = false, CancellationToken ct = default);

//        Task<PagedResult<ProjectCardDto>> ListCardsAsync(string projectId, string? columnId = null, string? assignee = null, string? label = null, DateTime? updatedSince = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default);
//        Task<OperationResult> AddCommentToCardAsync(string cardId, string commentBody, CancellationToken ct = default);
//        Task<IReadOnlyList<(string Author, string Body, DateTime CreatedAt)>> GetCardCommentsAsync(string cardId, CancellationToken ct = default);
//        Task<ProjectCardDto> ReconcileCardAsync(string cardId, CancellationToken ct = default);
//    }

//    public interface ICollaboratorService
//    {
//        Task<OperationResult> InviteCollaboratorAsync(string owner, string repo, string usernameOrEmail, CollaboratorPermission permission, TimeSpan? inviteExpiry = null, CancellationToken ct = default);
//        Task<OperationResult> RemoveCollaboratorAsync(string owner, string repo, string username, CancellationToken ct = default);
//        Task<IReadOnlyList<CollaboratorDto>> ListCollaboratorsAsync(string owner, string repo, CancellationToken ct = default);
//        Task<OperationResult> AddTeamToRepoAsync(string org, string teamSlug, string repo, CollaboratorPermission permission, CancellationToken ct = default);
//    }

//    public interface IContributionService
//    {
//        Task<ContributionSummaryDto> GetContributionSummaryAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);
//        Task<PagedResult<object>> ListContributionsAsync(string owner, string repo, string username, DateTime windowStart, DateTime windowEnd, int pageSize = 100, string? continuationToken = null, CancellationToken ct = default);
//        Task<IReadOnlyList<ContributionSummaryDto>> GetProjectContributionsAsync(string projectId, DateTime windowStart, DateTime windowEnd, CancellationToken ct = default);
//    }

//    public interface IPurgeService
//    {
//        Task<DeletionAuditDto> DeleteAndPurgeRepositoryAsync(string owner, string repo, string requestedBy, bool hardDeleteGitHubRepo = true, bool redactOnly = false, CancellationToken ct = default);
//        Task<OperationResult> BlockRepositoryAccessAsync(string owner, string repo, string requestedBy, CancellationToken ct = default);
//        Task<DeletionAuditDto?> GetDeletionAuditAsync(string jobId, CancellationToken ct = default);
//        bool IsRepositoryBlocked(string owner, string repo);
//    }

//    #endregion
//}
