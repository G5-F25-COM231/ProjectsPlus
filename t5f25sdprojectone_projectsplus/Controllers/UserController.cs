using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("v1/users")]
    public class UserController : ControllerBase
    {
        private const string CorrelationHeader = "X-Correlation-Id";
        private const string ExpectedVersionHeader = "X-Expected-Version";

        private readonly IUserService _userService;
        private readonly IAuthorizationService _authz;
        private readonly IAuditRepository _auditRepo;
        private readonly ILogger<UserController> _logger;

        public UserController(
            IUserService userService,
            IAuthorizationService authz,
            IAuditRepository auditRepo,
            ILogger<UserController> logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _auditRepo = auditRepo ?? throw new ArgumentNullException(nameof(auditRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // POST /v1/users
        // - Requires X-Correlation-Id for idempotency and external side-effects.
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Services.Interfaces.CreateUserRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader))
                return BadRequest(new { message = "Missing correlation header" });

            var correlationId = Request.Headers[CorrelationHeader].ToString();
            var ct = HttpContext.RequestAborted;

            var result = await _userService.CreateFromRequestAsync(req, correlationId, ct).ConfigureAwait(false);

            // Failure path: result.UserId may be null
            if (!result.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(result.UserId, "users.create", "deny", result.Reason ?? "create failed", ct).ConfigureAwait(false);
                return Conflict(new { message = result.Reason ?? "create failed" });
            }

            // Success path: defensively ensure UserId present
            if (!result.UserId.HasValue)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(null, "users.create", "deny", "create succeeded but missing id", ct).ConfigureAwait(false);
                _logger.LogError("CreateFromRequestAsync returned Succeeded=true but UserId was null for request {Request}", req);
                return StatusCode(500, new { message = "create succeeded but returned no id" });
            }

            var userId = result.UserId.Value;
            await _auditRepo.RecordAuthorizationAuditAsync(userId, "users.create", "allow", "create success", ct).ConfigureAwait(false);
            var profile = await _userService.GetProfileAsync(userId, ct).ConfigureAwait(false);
            return Created($"/v1/users/{userId}", profile);
        }

        // GET /v1/users/{id}
        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id)
        {
            var ct = HttpContext.RequestAborted;
            long? subjectId = null;
            if (long.TryParse(User.FindFirst("sub")?.Value, out var sid) && sid > 0) subjectId = sid;

            var authResult = await _authz.IsAuthorizedAsync(subjectId ?? 0, "users.view", "user", id, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.view", authResult.Allowed ? "allow" : "deny", authResult.ExplainText, ct).ConfigureAwait(false);
            if (!authResult.Allowed) return Forbid();

            var profile = await _userService.GetProfileAsync(id, ct).ConfigureAwait(false);
            if (profile == null) return NotFound();
            return Ok(profile);
        }

        // PATCH /v1/users/{id}
        // - Requires X-Correlation-Id
        [HttpPatch("{id:long}")]
        public async Task<IActionResult> Patch(long id, [FromBody] Services.Interfaces.PatchUserRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader))
                return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;
            long? subjectId = null;
            if (long.TryParse(User.FindFirst("sub")?.Value, out var sid) && sid > 0) subjectId = sid;

            var authResult = await _authz.IsAuthorizedAsync(subjectId ?? 0, "users.update", "user", id, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.update", authResult.Allowed ? "allow" : "deny", authResult.ExplainText, ct).ConfigureAwait(false);
            if (!authResult.Allowed) return Forbid();

            var patchResult = await _userService.PatchUserAsync(id, req, ct).ConfigureAwait(false);

            if (!patchResult.Succeeded)
            {
                if (patchResult.IsConcurrencyFailure)
                {
                    await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.update", "deny", "concurrency conflict", ct).ConfigureAwait(false);
                    return Conflict(new { message = "Concurrency conflict" });
                }

                await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.update", "deny", patchResult.Reason ?? "update failed", ct).ConfigureAwait(false);
                return BadRequest(new { message = patchResult.Reason ?? "update failed" });
            }

            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.update", "allow", "update success", ct).ConfigureAwait(false);
            var profile = await _userService.GetProfileAsync(id, ct).ConfigureAwait(false);
            return Ok(profile);
        }

        // DELETE /v1/users/{id}
        // - Requires X-Correlation-Id and X-Expected-Version header
        [HttpDelete("{id:long}")]
        public async Task<IActionResult> Delete(long id)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader))
                return BadRequest(new { message = "Missing correlation header" });

            if (!Request.Headers.ContainsKey(ExpectedVersionHeader))
                return BadRequest(new { message = "Missing expected version header" });

            if (!int.TryParse(Request.Headers[ExpectedVersionHeader].ToString(), out var expectedVersion))
                return BadRequest(new { message = "Invalid expected version header" });

            var ct = HttpContext.RequestAborted;
            if (!long.TryParse(User.FindFirst("sub")?.Value, out var subjectId)) subjectId = 0;

            var authResult = await _authz.IsAuthorizedAsync(subjectId, "users.delete", "user", id, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.delete", authResult.Allowed ? "allow" : "deny", authResult.ExplainText, ct).ConfigureAwait(false);
            if (!authResult.Allowed) return Forbid();

            // The service DeleteUserAsync matches signature: (long id, int expectedVersion, CancellationToken ct)
            await _userService.DeleteUserAsync(id, expectedVersion, ct).ConfigureAwait(false);

            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.delete", "allow", "delete success", ct).ConfigureAwait(false);
            return NoContent();
        }

        // POST /v1/users/apply
        // Domain-specific action that may change other users or external providers; requires correlationId
        [HttpPost("apply")]
        public async Task<IActionResult> Apply([FromBody] Services.Interfaces.ApplyUsersRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader))
                return BadRequest(new { message = "Missing correlation header" });

            var correlationId = Request.Headers[CorrelationHeader].ToString();
            var ct = HttpContext.RequestAborted;
            if (!long.TryParse(User.FindFirst("sub")?.Value, out var subjectId)) subjectId = 0;

            var authResult = await _authz.IsAuthorizedAsync(subjectId, "users.apply", "user", null, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.apply", authResult.Allowed ? "allow" : "deny", authResult.ExplainText, ct).ConfigureAwait(false);
            if (!authResult.Allowed) return Forbid();

            var applyResult = await _userService.ApplyUsersAsync(req, correlationId, ct).ConfigureAwait(false);
            if (!applyResult.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.apply", "deny", applyResult.Reason ?? "apply failed", ct).ConfigureAwait(false);
                return BadRequest(new { message = applyResult.Reason ?? "apply failed" });
            }

            await _auditRepo.RecordAuthorizationAuditAsync(subjectId, "users.apply", "allow", "apply success", ct).ConfigureAwait(false);
            return Ok(applyResult.Summary);
        }
    }
}
