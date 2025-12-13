// src/Controllers/AuthController.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("v1/auth")]
    public class AuthController : ControllerBase
    {
        private const string CorrelationHeader = "X-Correlation-Id";

        private readonly IAuthManager _authManager;
        private readonly IAuthorizationService _authz;
        private readonly IAuditRepository _auditRepo;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthManager authManager,
            IAuthorizationService authz,
            IAuditRepository auditRepo,
            ILogger<AuthController> logger)
        {
            _authManager = authManager ?? throw new ArgumentNullException(nameof(authManager));
            _authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _auditRepo = auditRepo ?? throw new ArgumentNullException(nameof(auditRepo));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // POST /v1/auth/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;
            var result = await _authManager.ValidateCredentialsAsync(req.Username, req.Password, ct).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(null, "auth.login", "deny", result.Reason ?? "invalid credentials", ct).ConfigureAwait(false);
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var tokens = await _authManager.IssueTokensAsync(result.UserId, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(result.UserId, "auth.login", "allow", "login success", ct).ConfigureAwait(false);
            return Ok(tokens);
        }

        // POST /v1/auth/refresh
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;
            var res = await _authManager.RefreshAsync(req.RefreshToken, ct).ConfigureAwait(false);

            if (!res.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(null, "auth.refresh", "deny", res.Reason ?? "invalid refresh", ct).ConfigureAwait(false);
                return Unauthorized(new { message = "Invalid refresh token" });
            }

            await _auditRepo.RecordAuthorizationAuditAsync(res.UserId, "auth.refresh", "allow", "refresh success", ct).ConfigureAwait(false);
            return Ok(res.Tokens);
        }

        // GET /v1/auth/me
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;

            if (!long.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId <= 0)
            {
                return Unauthorized();
            }

            // Use refined authorization contract: check action "users.view" on resourceType "user" and resourceId = userId
            var authResult = await _authz.IsAuthorizedAsync(userId, "users.view", "user", userId, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(userId, "auth.me", authResult.Allowed ? "allow" : "deny", authResult.ExplainText, ct).ConfigureAwait(false);

            if (!authResult.Allowed) return Forbid();

            var profile = await _authManager.GetProfileAsync(userId, ct).ConfigureAwait(false);
            return Ok(profile);
        }

        // POST /v1/auth/logout
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;

            if (!long.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId <= 0)
            {
                return Unauthorized();
            }

            await _authManager.RevokeAsync(userId, req.RevokeRefreshToken, ct).ConfigureAwait(false);
            await _auditRepo.RecordAuthorizationAuditAsync(userId, "auth.logout", "allow", "logout", ct).ConfigureAwait(false);
            return NoContent();
        }

        // POST /v1/auth/perm/eval
        [HttpPost("perm/eval")]
        public async Task<IActionResult> EvaluatePermission([FromBody] PermissionEvalRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var ct = HttpContext.RequestAborted;

            // Evaluate using the refined contract
            var peResult = await _authz.EvaluateAsync(req, ct).ConfigureAwait(false);

            // Record audit (best-effort)
            await _auditRepo.RecordAuthorizationAuditAsync(req.userId, "auth.perm.eval", peResult.Allowed ? "allow" : "deny", peResult.Reason ?? string.Empty, ct).ConfigureAwait(false);

            if (peResult.Allowed) return Ok(new { allowed = true, reason = peResult.Reason, grantId = peResult.GrantId });
            return Forbid();
        }
    }

    // --- DTOs used by the controller --- (keep minimal and explicit)
    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string RefreshToken);
    public record LogoutRequest(bool RevokeRefreshToken);
}
