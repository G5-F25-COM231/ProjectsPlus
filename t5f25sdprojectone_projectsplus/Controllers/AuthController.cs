// src/Controllers/AuthController.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Authorization.Interfaces;

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

            var result = await _authManager.ValidateCredentialsAsync(req.Username, req.Password);
            if (!result.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(null, "auth.login", "deny", result.Reason ?? "invalid credentials");
                return Unauthorized(new { message = "Invalid credentials" });
            }

            var tokens = await _authManager.IssueTokensAsync(result.UserId);
            await _auditRepo.RecordAuthorizationAuditAsync(result.UserId, "auth.login", "allow", "login success");
            return Ok(tokens);
        }

        // POST /v1/auth/refresh
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            var res = await _authManager.RefreshAsync(req.RefreshToken);
            if (!res.Succeeded)
            {
                await _auditRepo.RecordAuthorizationAuditAsync(null, "auth.refresh", "deny", res.Reason ?? "invalid refresh");
                return Unauthorized(new { message = "Invalid refresh token" });
            }

            await _auditRepo.RecordAuthorizationAuditAsync(res.UserId, "auth.refresh", "allow", "refresh success");
            return Ok(res.Tokens);
        }

        // GET /v1/auth/me
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            // assume middleware has populated User claims; fall back to token introspection
            if (!long.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId <= 0)
            {
                return Unauthorized();
            }

            var isAuthenticated = await _authz.IsAuthenticatedAsync(userId);
            if (!isAuthenticated) return Unauthorized();

            var profile = await _authManager.GetProfileAsync(userId);
            return Ok(profile);
        }

        // POST /v1/auth/logout
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest req)
        {
            if (!Request.Headers.ContainsKey(CorrelationHeader)) return BadRequest(new { message = "Missing correlation header" });

            if (!long.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId <= 0)
            {
                return Unauthorized();
            }

            // revoke tokens (idempotent)
            await _authManager.RevokeAsync(userId, req.RevokeRefreshToken);
            await _auditRepo.RecordAuthorizationAuditAsync(userId, "auth.logout", "allow", "logout");
            return NoContent();
        }
    }

    // --- DTOs used by the controller --- (keep minimal and explicit)
    public record LoginRequest(string Username, string Password);
    public record RefreshRequest(string RefreshToken);
    public record LogoutRequest(bool RevokeRefreshToken);

    // --- Minimal interfaces referenced by the controller ---
    // Implementations should be provided in DI composition for production and test replacements in component tests.

    public interface IAuthManager
    {
        Task<(bool Succeeded, long UserId, string? Reason)> ValidateCredentialsAsync(string username, string password);
        Task<TokenResponse> IssueTokensAsync(long userId);
        Task<(bool Succeeded, long UserId, string? Reason, TokenResponse? Tokens)> RefreshAsync(string refreshToken);
        Task RevokeAsync(long userId, bool revokeRefreshToken);
        Task<UserProfile> GetProfileAsync(long userId);
    }

    public record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);
    public record UserProfile(long Id, string Username, string Email);
}
