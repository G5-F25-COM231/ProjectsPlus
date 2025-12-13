// src/ProjectsPlus.GitHub/Controllers/GitHController.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.GithubService;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    /// <summary>
    /// HTTP API surface for ProjectsPlus GitHub operations.
    /// This controller is a thin HTTP layer that delegates to the GitHubManager service partials.
    /// It intentionally performs minimal business logic and focuses on validation, mapping, and correct HTTP responses.
    /// </summary>
    [ApiController]
    [Route("api/github")]
    public class GitHController : ControllerBase
    {
        private readonly IGitHubRepositoryManager _repos;
        private readonly IProjectBoardService _projects;
        private readonly ICollaboratorService _collabs;
        private readonly IContributionService _contribs;
        private readonly IPurgeService _purge;
        private readonly ILogger<GitHController> _logger;

        public GitHController(
            IGitHubRepositoryManager repos,
            IProjectBoardService projects,
            ICollaboratorService collabs,
            IContributionService contribs,
            IPurgeService purge,
            ILogger<GitHController> logger)
        {
            _repos = repos ?? throw new ArgumentNullException(nameof(repos));
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _collabs = collabs ?? throw new ArgumentNullException(nameof(collabs));
            _contribs = contribs ?? throw new ArgumentNullException(nameof(contribs));
            _purge = purge ?? throw new ArgumentNullException(nameof(purge));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Repository endpoints

        /// <summary>
        /// Create a repository under the authenticated user or an organization (if Owner provided in body).
        /// </summary>
        [HttpPost("repos")]
        public async Task<ActionResult<RepositoryDto>> CreateRepository([FromBody] RepositoryDto repoSpec, CancellationToken ct)
        {
            if (repoSpec == null) return BadRequest("Repository specification is required.");
            if (string.IsNullOrWhiteSpace(repoSpec.Name)) return BadRequest("Repository name is required.");

            try
            {
                var created = await _repos.CreateRepositoryAsync(repoSpec, initializeWithReadme: true, ct).ConfigureAwait(false);
                return CreatedAtAction(nameof(GetRepository), new { owner = created.Owner, name = created.Name }, created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateRepository failed for {Repo}", repoSpec?.Name);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Get repository metadata.
        /// </summary>
        [HttpGet("repos/{owner}/{name}")]
        public async Task<ActionResult<RepositoryDto>> GetRepository(string owner, string name, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) return BadRequest("owner and name are required.");

            try
            {
                var repo = await _repos.GetRepositoryAsync(owner, name, ct).ConfigureAwait(false);
                if (repo == null) return NotFound();
                return Ok(repo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRepository failed for {Owner}/{Name}", owner, name);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// List repositories for a user or organization.
        /// </summary>
        [HttpGet("repos/{ownerOrOrg}")]
        public async Task<ActionResult<PagedResult<RepositoryDto>>> ListRepositories(string ownerOrOrg, [FromQuery] int pageSize = 100, [FromQuery] string? page = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(ownerOrOrg)) return BadRequest("ownerOrOrg is required.");

            try
            {
                var result = await _repos.ListRepositoriesAsync(ownerOrOrg, pageSize, page, ct).ConfigureAwait(false);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListRepositories failed for {OwnerOrOrg}", ownerOrOrg);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Delete a repository. This will attempt to delete the GitHub repo (if credentials allow) and return an OperationResult.
        /// </summary>
        [HttpDelete("repos/{owner}/{name}")]
        public async Task<ActionResult<OperationResult>> DeleteRepository(string owner, string name, [FromQuery] string requestedBy = "unknown", [FromQuery] bool purgeProjectsPlusRecords = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name)) return BadRequest("owner and name are required.");
            if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "unknown";

            try
            {
                var res = await _repos.DeleteRepositoryAsync(owner, name, requestedBy, purgeProjectsPlusRecords, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                if (res.ErrorCode == "NotFound") return NotFound(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteRepository failed for {Owner}/{Name}", owner, name);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Apply branch protection to a branch.
        /// </summary>
        [HttpPut("repos/{owner}/{repo}/branches/protect")]
        public async Task<ActionResult<OperationResult>> ProtectBranch(string owner, string repo, [FromBody] BranchProtectionDto protection, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return BadRequest("owner and repo are required.");
            if (protection == null) return BadRequest("protection payload required.");

            try
            {
                var res = await _repos.ProtectBranchAsync(owner, repo, protection, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProtectBranch failed for {Owner}/{Repo}", owner, repo);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        #endregion

        #region Project board endpoints (classic Projects)

        /// <summary>
        /// Create a classic project. If repositoryFullName is provided (owner/repo) the project is created scoped to that repo.
        /// </summary>
        [HttpPost("projects")]
        public async Task<ActionResult<ProjectDto>> CreateProject([FromQuery] ProjectType type = ProjectType.Classic, [FromBody] ProjectDto? request = null, [FromQuery] string? repositoryFullName = null, CancellationToken ct = default)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Project name is required.");

            try
            {
                var created = await _projects.CreateProjectAsync(request.Name, type, repositoryFullName, ct).ConfigureAwait(false);
                return CreatedAtAction(nameof(GetProject), new { projectId = created.ProjectId }, created);
            }
            catch (NotSupportedException ns)
            {
                return BadRequest(ns.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateProject failed for {Name}", request?.Name);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Get a project by id.
        /// </summary>
        [HttpGet("projects/{projectId}")]
        public async Task<ActionResult<ProjectDto>> GetProject(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest("projectId required.");

            try
            {
                var p = await _projects.GetProjectAsync(projectId, ct).ConfigureAwait(false);
                if (p == null) return NotFound();
                return Ok(p);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetProject failed for {ProjectId}", projectId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Delete a project.
        /// </summary>
        [HttpDelete("projects/{projectId}")]
        public async Task<ActionResult<OperationResult>> DeleteProject(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest("projectId required.");

            try
            {
                var res = await _projects.DeleteProjectAsync(projectId, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                if (res.ErrorCode == "NotFound") return NotFound(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteProject failed for {ProjectId}", projectId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Create a column in a project.
        /// </summary>
        [HttpPost("projects/{projectId}/columns")]
        public async Task<ActionResult<ProjectColumnDto>> CreateColumn(string projectId, [FromBody] string columnName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest("projectId required.");
            if (string.IsNullOrWhiteSpace(columnName)) return BadRequest("columnName required.");

            try
            {
                var col = await _projects.CreateColumnAsync(projectId, columnName, ct).ConfigureAwait(false);
                return CreatedAtAction(nameof(ListColumns), new { projectId }, col);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateColumn failed for {ProjectId}", projectId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// List columns for a project.
        /// </summary>
        [HttpGet("projects/{projectId}/columns")]
        public async Task<ActionResult<IReadOnlyList<ProjectColumnDto>>> ListColumns(string projectId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(projectId)) return BadRequest("projectId required.");

            try
            {
                var cols = await _projects.ListColumnsAsync(projectId, ct).ConfigureAwait(false);
                return Ok(cols);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListColumns failed for {ProjectId}", projectId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Create a project card.
        /// </summary>
        [HttpPost("projects/cards")]
        public async Task<ActionResult<ProjectCardDto>> CreateCard([FromBody] ProjectCardDto createRequest, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null, CancellationToken ct = default)
        {
            if (createRequest == null) return BadRequest("createRequest required.");
            if (string.IsNullOrWhiteSpace(createRequest.ProjectId) || string.IsNullOrWhiteSpace(createRequest.ColumnId)) return BadRequest("ProjectId and ColumnId are required.");

            try
            {
                var card = await _projects.CreateCardAsync(createRequest, idempotencyKey, ct).ConfigureAwait(false);
                return CreatedAtAction(nameof(GetCard), new { cardId = card.CardId }, card);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateCard failed for project {ProjectId}", createRequest?.ProjectId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Get a project card by id.
        /// </summary>
        [HttpGet("projects/cards/{cardId}")]
        public async Task<ActionResult<ProjectCardDto>> GetCard(string cardId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return BadRequest("cardId required.");

            try
            {
                var card = await _projects.GetCardAsync(cardId, ct).ConfigureAwait(false);
                if (card == null) return NotFound();
                return Ok(card);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCard failed for {CardId}", cardId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Update a project card (note/body). Use If-Match header to provide ETag for concurrency if supported.
        /// </summary>
        [HttpPatch("projects/cards/{cardId}")]
        public async Task<ActionResult<ProjectCardDto>> UpdateCard(string cardId, [FromBody] ProjectCardDto updateRequest, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return BadRequest("cardId required.");
            if (updateRequest == null) return BadRequest("updateRequest required.");
            if (cardId != updateRequest.CardId) return BadRequest("cardId mismatch.");

            var ifMatch = Request.Headers["If-Match"].FirstOrDefault();

            try
            {
                var updated = await _projects.UpdateCardAsync(updateRequest, ifMatch, ct).ConfigureAwait(false);
                return Ok(updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateCard failed for {CardId}", cardId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Move a card to another column.
        /// </summary>
        [HttpPost("projects/cards/{cardId}/move")]
        public async Task<ActionResult<OperationResult>> MoveCard(string cardId, [FromQuery] string targetColumnId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(targetColumnId)) return BadRequest("cardId and targetColumnId required.");

            try
            {
                var res = await _projects.MoveCardAsync(cardId, targetColumnId, null, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MoveCard failed for {CardId}", cardId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Delete or archive a card.
        /// </summary>
        [HttpDelete("projects/cards/{cardId}")]
        public async Task<ActionResult<OperationResult>> DeleteCard(string cardId, [FromQuery] bool archiveOnly = true, [FromQuery] bool deleteLinkedIssue = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cardId)) return BadRequest("cardId required.");

            try
            {
                var res = await _projects.DeleteCardAsync(cardId, archiveOnly, deleteLinkedIssue, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                if (res.ErrorCode == "NotFound") return NotFound(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteCard failed for {CardId}", cardId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        #endregion

        #region Collaborator endpoints

        /// <summary>
        /// Invite a collaborator to a repository.
        /// </summary>
        [HttpPut("repos/{owner}/{repo}/collaborators/{usernameOrEmail}")]
        public async Task<ActionResult<OperationResult>> InviteCollaborator(string owner, string repo, string usernameOrEmail, [FromQuery] CollaboratorPermission permission = CollaboratorPermission.Write, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(usernameOrEmail)) return BadRequest("owner, repo and usernameOrEmail required.");

            try
            {
                var res = await _collabs.InviteCollaboratorAsync(owner, repo, usernameOrEmail, permission, null, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "InviteCollaborator failed for {Owner}/{Repo} -> {User}", owner, repo, usernameOrEmail);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Remove a collaborator from a repository.
        /// </summary>
        [HttpDelete("repos/{owner}/{repo}/collaborators/{username}")]
        public async Task<ActionResult<OperationResult>> RemoveCollaborator(string owner, string repo, string username, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(username)) return BadRequest("owner, repo and username required.");

            try
            {
                var res = await _collabs.RemoveCollaboratorAsync(owner, repo, username, ct).ConfigureAwait(false);
                if (res.Success) return Ok(res);
                if (res.ErrorCode == "NotFound") return NotFound(res);
                return StatusCode(500, res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveCollaborator failed for {Owner}/{Repo} -> {User}", owner, repo, username);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// List collaborators for a repository.
        /// </summary>
        [HttpGet("repos/{owner}/{repo}/collaborators")]
        public async Task<ActionResult<IReadOnlyList<CollaboratorDto>>> ListCollaborators(string owner, string repo, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return BadRequest("owner and repo required.");

            try
            {
                var list = await _collabs.ListCollaboratorsAsync(owner, repo, ct).ConfigureAwait(false);
                return Ok(list);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListCollaborators failed for {Owner}/{Repo}", owner, repo);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        #endregion

        #region Contributions endpoints

        /// <summary>
        /// Get a contribution summary for a user in a repo within a time window.
        /// </summary>
        [HttpGet("repos/{owner}/{repo}/contributions/{username}/summary")]
        public async Task<ActionResult<ContributionSummaryDto>> GetContributionSummary(string owner, string repo, string username, [FromQuery] DateTime windowStart, [FromQuery] DateTime windowEnd, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(username)) return BadRequest("owner, repo and username required.");
            if (windowEnd <= windowStart) return BadRequest("windowEnd must be after windowStart.");

            try
            {
                var summary = await _contribs.GetContributionSummaryAsync(owner, repo, username, windowStart, windowEnd, ct).ConfigureAwait(false);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetContributionSummary failed for {Owner}/{Repo} user {User}", owner, repo, username);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// List contributions (raw events) for a user in a repo within a time window.
        /// </summary>
        [HttpGet("repos/{owner}/{repo}/contributions/{username}")]
        public async Task<ActionResult<PagedResult<object>>> ListContributions(string owner, string repo, string username, [FromQuery] DateTime windowStart, [FromQuery] DateTime windowEnd, [FromQuery] int pageSize = 100, [FromQuery] string? page = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(username)) return BadRequest("owner, repo and username required.");
            if (windowEnd <= windowStart) return BadRequest("windowEnd must be after windowStart.");

            try
            {
                var paged = await _contribs.ListContributionsAsync(owner, repo, username, windowStart, windowEnd, pageSize, page, ct).ConfigureAwait(false);
                return Ok(paged);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListContributions failed for {Owner}/{Repo} user {User}", owner, repo, username);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        #endregion

        #region Purge and audit endpoints

        /// <summary>
        /// Delete and purge a repository from GitHub and ProjectsPlus (or redact only).
        /// Returns a DeletionAuditDto describing the outcome.
        /// </summary>
        [HttpPost("repos/{owner}/{repo}/delete-and-purge")]
        public async Task<ActionResult<DeletionAuditDto>> DeleteAndPurgeRepository(string owner, string repo, [FromQuery] string requestedBy = "unknown", [FromQuery] bool hardDeleteGitHubRepo = true, [FromQuery] bool redactOnly = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return BadRequest("owner and repo required.");
            if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "unknown";

            try
            {
                var audit = await _purge.DeleteAndPurgeRepositoryAsync(owner, repo, requestedBy, hardDeleteGitHubRepo, redactOnly, ct).ConfigureAwait(false);
                return Ok(audit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteAndPurgeRepository failed for {Owner}/{Repo}", owner, repo);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Block repository access inside ProjectsPlus (soft block).
        /// </summary>
        [HttpPost("repos/{owner}/{repo}/block")]
        public async Task<ActionResult<OperationResult>> BlockRepositoryAccess(string owner, string repo, [FromQuery] string requestedBy = "unknown", CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) return BadRequest("owner and repo required.");
            if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "unknown";

            try
            {
                var res = await _purge.BlockRepositoryAccessAsync(owner, repo, requestedBy, ct).ConfigureAwait(false);
                return Ok(res);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BlockRepositoryAccess failed for {Owner}/{Repo}", owner, repo);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        /// <summary>
        /// Get deletion audit by job id.
        /// </summary>
        [HttpGet("deletions/{jobId}")]
        public async Task<ActionResult<DeletionAuditDto>> GetDeletionAudit(string jobId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return BadRequest("jobId required.");

            try
            {
                var audit = await _purge.GetDeletionAuditAsync(jobId, ct).ConfigureAwait(false);
                if (audit == null) return NotFound();
                return Ok(audit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDeletionAudit failed for {JobId}", jobId);
                return Problem(detail: ex.Message, statusCode: 500);
            }
        }

        #endregion
    }
}
