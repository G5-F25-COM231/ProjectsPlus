using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.Interfaces;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Commons;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using IAuthorizationService = t5f25sdprojectone_projectsplus.Services.Interfaces.IAuthorizationService;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("v1/projects")]
    public class ProjectsController : ControllerBase
    {
        private readonly IProjectService _projects;
        private readonly IAuthorizationService _auth;
        private readonly CorrelationOptions _corr;
        private readonly ILogger<ProjectsController> _logger;

        // Constructor requires services wired via DI; logger helps trace authorization/audit decisions.
        public ProjectsController(IProjectService projects, IAuthorizationService auth, CorrelationOptions corr, ILogger<ProjectsController> logger)
        {
            _projects = projects ?? throw new ArgumentNullException(nameof(projects));
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _corr = corr ?? throw new ArgumentNullException(nameof(corr));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // POST /v1/projects
        // - Requires correlation header when operation can affect external systems (infra/orchestrator)
        // - Propagates correlationId into ProjectEntity.CorrelationId for downstream tracing
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProjectEntity request, CancellationToken ct = default)
        {
            ct = ct == default ? HttpContext.RequestAborted : ct;

            // validate correlation header per Phase 6 rules
            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            var correlationId = Request.Headers[_corr.HeaderName].ToString();

            // caller user id (subject) extracted from claims; used for authorization and audits
            var userId = ParseUserId();
            var authResult = await _auth.IsAuthorizedAsync(userId ?? 0, "Project.Create", "Project", null, ct).ConfigureAwait(false);
            if (!authResult.Allowed)
            {
                _logger.LogInformation("Project.Create denied for user {UserId} correlation {Correlation}", userId, correlationId);
                return Forbid();
            }

            // ensure correlation propagated for orchestrator / downstream systems
            request.CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? correlationId : request.CorrelationId;

            // Service handles validation, persistence, audit writes, and orchestration handoff
            var created = await _projects.CreateProjectAsync(request, ct).ConfigureAwait(false);

            // CreatedAtAction produces Location header; controller surface remains small and testable
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        // PATCH /v1/projects/{id}
        // - Requires correlation header
        // - Controller sets id on incoming entity, propagates correlation id, and delegates concurrency handling to service
        [HttpPatch("{id:long}")]
        public async Task<IActionResult> Patch(long id, [FromBody] ProjectEntity patch, CancellationToken ct = default)
        {
            ct = ct == default ? HttpContext.RequestAborted : ct;

            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            var correlationId = Request.Headers[_corr.HeaderName].ToString();

            var userId = ParseUserId();
            var authResult = await _auth.IsAuthorizedAsync(userId ?? 0, "Project.Edit", "Project", id, ct).ConfigureAwait(false);
            if (!authResult.Allowed)
            {
                _logger.LogInformation("Project.Edit denied for user {UserId} project {ProjectId} correlation {Correlation}", userId, id, correlationId);
                return Forbid();
            }

            // enforce id and correlation on the patch object before handing to service
            patch.Id = id;
            patch.CorrelationId = string.IsNullOrWhiteSpace(patch.CorrelationId) ? correlationId : patch.CorrelationId;

            // service will throw or return an appropriate domain exception on concurrency; controller can map if needed
            var updated = await _projects.UpdateProjectAsync(patch, ct).ConfigureAwait(false);
            return Ok(updated);
        }

        // POST /v1/projects/{id}/submit
        // - Requires correlation header and expectedVersion (query) to enforce optimistic concurrency
        // - Submit ties to orchestrator; controller validates submitter and delegates
        [HttpPost("{id:long}/submit")]
        public async Task<IActionResult> Submit(long id, [FromQuery] int expectedVersion, [FromBody] SubmitRequest body, CancellationToken ct = default)
        {
            ct = ct == default ? HttpContext.RequestAborted : ct;

            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            var correlationId = Request.Headers[_corr.HeaderName].ToString();

            var userId = ParseUserId();
            var authResult = await _auth.IsAuthorizedAsync(userId ?? 0, "Project.Submit", "Project", id, ct).ConfigureAwait(false);
            if (!authResult.Allowed)
            {
                _logger.LogInformation("Project.Submit denied for user {UserId} project {ProjectId} correlation {Correlation}", userId, id, correlationId);
                return Forbid();
            }

            // validate submitter id shape and convert to long; controller keeps DTO flexible but validates
            if (!long.TryParse(body.SubmitterUserId, out var submitterId) || submitterId <= 0)
                return BadRequest(new { error = "Invalid SubmitterUserId" });

            // delegate to service which will handle audit/orchestration
            var result = await _projects.SubmitProjectAsync(id, expectedVersion, submitterId.ToString(), body.Comment, ct).ConfigureAwait(false);
            return Ok(result);
        }

        // GET /v1/projects/{id}
        // - Authorization enforced; returns 404 when service returns null
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct = default)
        {
            ct = ct == default ? HttpContext.RequestAborted : ct;

            var userId = ParseUserId();
            var authResult = await _auth.IsAuthorizedAsync(userId ?? 0, "Project.View", "Project", id, ct).ConfigureAwait(false);
            if (!authResult.Allowed)
            {
                _logger.LogInformation("Project.View denied for user {UserId} project {ProjectId}", userId, id);
                return Forbid();
            }

            var view = await _projects.GetProjectViewAsync(id, ct).ConfigureAwait(false);
            if (view == null) return NotFound();
            return Ok(view);
        }

        // helper to extract logged-in user's numeric id from the sub claim
        private long? ParseUserId()
        {
            if (long.TryParse(User.FindFirst("sub")?.Value, out var sid) && sid > 0) return sid;
            return null;
        }

        // SubmitRequest DTO kept minimal; validation performed in action
        public sealed class SubmitRequest
        {
            // submitter id expected to be numeric string; change to long if you prefer stricter typing
            public string SubmitterUserId { get; set; } = string.Empty;
            public string? Comment { get; set; }
        }
    }
}


// src/Controllers/ProjectsController.cs
// Phase 6 — Controllers (start with core small surface)
// Deliver controllers one at a time, wire into DI, with component tests using in-memory DB and mocked endpoints.
// - This controller implements a small surface: create, patch, submit, get
// - Rules applied: validate correlation header for operations that affect external systems;
//   use caller user id (subjectId) from claims for authorization checks; write audits via service/repo elsewhere
// Notes:
// 1) POST /v1/projects should propagate correlationId into ProjectEntity for downstream orchestrator.
// 2) Projects/ProjectTypes and heavy infra work should be delegated to an InfraOrchestrator stub in tests.
// 3) Acceptance: end-to-end create/write/submit must produce audit/state change entries (handled by services).
// 4) Controller defaults CancellationToken to HttpContext.RequestAborted so tests can omit ct.
//
// Tests to exercise (recommended):
// - create project with X-Correlation-Id present
// - patch with X-Correlation-Id and verify optimistic concurrency 409 handled by service
// - submit with expectedVersion and correlation header; verify orchestration/audit side-effects