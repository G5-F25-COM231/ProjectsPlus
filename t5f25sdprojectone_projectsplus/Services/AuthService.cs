// src/Services/Auth/AuthService.cs
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using t5f25sdprojectone_projectsplus.DTOs.UserDTOs;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    /// <summary>
    /// AuthService implements both IAuthManager (authentication/token lifecycle)
    /// and IAuthorizationService (permission evaluation) using a conservative, auditable
    /// default policy suitable for tests and gradual replacement by a policy engine.
    /// </summary>
    public class AuthService : IAuthManager, IAuthorizationService
    {
        private readonly IAuthRepository _repo;
        private readonly IAuditRepository _audit;
        private readonly AuthOptions _options;
        private readonly ILogger<AuthService> _logger;

        public AuthService() { }
        public AuthService(IAuthRepository repo, IAuditRepository audit, IOptions<AuthOptions>? options = null, ILogger<AuthService>? logger = null)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
            _options = options?.Value ?? new AuthOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ------------------ IAuthManager (authentication / token lifecycle) ------------------

        public async Task<(bool Succeeded, long UserId, string? Reason)> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, 0, "missing credentials");

            var user = await _repo.FindByUsernameAsync(username, ct).ConfigureAwait(false);
            if (user == null) return (false, 0, "invalid credentials");

            if (!VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
                return (false, 0, "invalid credentials");

            if (!user.IsActive) return (false, 0, "user disabled");

            _logger.LogInformation("AuthService: validated credentials for user {UserId}", user.Id);
            return (true, user.Id, null);
        }

        public async Task<TokenResponse> IssueTokensAsync(long userId, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow;
            var accessExpiry = now.AddSeconds(_options.AccessTokenLifetimeSeconds);
            var accessToken = CreateOpaqueToken(userId, accessExpiry);

            var refreshToken = CreateSecureRandomToken();
            var refreshExpiry = now.AddSeconds(_options.RefreshTokenLifetimeSeconds);

            await _repo.PersistRefreshTokenAsync(userId, refreshToken, refreshExpiry.UtcDateTime, ct).ConfigureAwait(false);
            _logger.LogInformation("AuthService: issued tokens for user {UserId} accessExp={AccessExp} refreshExp={RefreshExp}", userId, accessExpiry, refreshExpiry);

            return new TokenResponse(accessToken, refreshToken, _options.AccessTokenLifetimeSeconds);
        }

        public async Task<(bool Succeeded, long UserId, string? Reason, TokenResponse? Tokens)> RefreshAsync(string refreshToken, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(refreshToken)) return (false, 0, "missing refresh token", null);

            var record = await _repo.FindRefreshTokenAsync(refreshToken, ct).ConfigureAwait(false);
            if (record == null)
            {
                _logger.LogWarning("AuthService: refresh failed - token not found");
                return (false, 0, "invalid refresh token", null);
            }

            if (record.IsRevoked)
            {
                _logger.LogWarning("AuthService: refresh failed - token revoked for user {UserId}", record.UserId);
                return (false, 0, "refresh token revoked", null);
            }

            if (record.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _logger.LogWarning("AuthService: refresh failed - token expired for user {UserId}", record.UserId);
                return (false, record.UserId, "refresh token expired", null);
            }

            await _repo.RevokeRefreshTokenAsync(refreshToken, ct).ConfigureAwait(false);
            var tokens = await IssueTokensAsync(record.UserId, ct).ConfigureAwait(false);

            _logger.LogInformation("AuthService: refreshed tokens for user {UserId}", record.UserId);
            return (true, record.UserId, null, tokens);
        }

        public async Task RevokeAsync(long userId, bool revokeRefreshToken, CancellationToken ct = default)
        {
            if (revokeRefreshToken)
            {
                await _repo.RevokeAllRefreshTokensForUserAsync(userId, ct).ConfigureAwait(false);
                _logger.LogInformation("AuthService: revoked refresh tokens for user {UserId}", userId);
            }
        }

        public async Task<UserProfile> GetProfileAsync(long userId, CancellationToken ct = default)
        {
            var user = await _repo.FindByIdAsync(userId, ct).ConfigureAwait(false);
            if (user == null) throw new InvalidOperationException($"User {userId} not found");
            return UserProfile.FromEntity(user);
        }

        // ------------------ IAuthorizationService (permission evaluation) ------------------

        /// <summary>
        /// Conservative default: deny unless an explicit allow rule applies.
        /// - Self-scoped safe actions are allowed (users.view, users.update for self).
        /// - system.admin role grants everything.
        /// - All other actions are denied.
        /// Implementations should replace this with a proper policy engine.
        /// </summary>
        public async Task<AuthorizationResult> IsAuthorizedAsync(long userId, string action, string? resourceType = null, long? resourceId = null, CancellationToken ct = default)
        {
            // Load user and quick rejects
            var user = await _repo.FindByIdAsync(userId, ct).ConfigureAwait(false);
            if (user == null || !user.IsActive)
                return AuthorizationResult.Deny("user not found or inactive", "user.notfound");

            // system admin override
            if (user.Roles != null && user.Roles.Contains("system.admin"))
                return AuthorizationResult.Allow("system admin", "role.system.admin");

            // Self actions when resourceType == "user" and resourceId matches
            if (string.Equals(resourceType, "user", StringComparison.OrdinalIgnoreCase) && resourceId.HasValue && resourceId.Value == userId)
            {
                return action switch
                {
                    "users.view" => AuthorizationResult.Allow("self view", "self.users.view"),
                    "users.update" => AuthorizationResult.Allow("self update (restricted fields)", "self.users.update"),
                    _ => AuthorizationResult.Deny("action not allowed on self", "self.default.deny")
                };
            }

            // Example workspace admin via Attributes stored in Roles (placeholder)
            // Real implementation should consult grants with attributes and expiry.
            if (user.Roles != null && user.Roles.Contains("workspace.admin") && resourceType == "workspace")
            {
                return AuthorizationResult.Allow("workspace admin", "role.workspace.admin");
            }

            // Default deny
            return AuthorizationResult.Deny("no matching allow rule", "default.deny");
        }

        public async Task<IReadOnlyDictionary<long, AuthorizationResult>> IsAuthorizedBatchAsync(long userId, string action, string resourceType, IReadOnlyList<long> resourceIds, CancellationToken ct = default)
        {
            var results = new Dictionary<long, AuthorizationResult>(resourceIds.Count);
            foreach (var id in resourceIds)
            {
                var r = await IsAuthorizedAsync(userId, action, resourceType, id, ct).ConfigureAwait(false);
                results[id] = r;
            }

            return results;
        }

        public Task InvalidateUserCacheAsync(long userId)
        {
            // No cache in this simple implementation; noop for compatibility
            _logger.LogDebug("AuthService: InvalidateUserCacheAsync({UserId}) noop", userId);
            return Task.CompletedTask;
        }

        /// <summary>
        /// New signature: accepts PermissionEvalRequest and returns PermissionEvaluationResult.
        /// This maps the richer AuthorizationResult into the lighter PermissionEvaluationResult used by controllers.
        /// </summary>
        public async Task<PermissionEvaluationResult> IsAuthorizedAsync(PermissionEvalRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            // Try parse resource id into long when possible (resource id may be non-numeric; map to null)
            long? numericResourceId = null;
            if (long.TryParse(req.ResourceId, out var parsed)) numericResourceId = parsed;

            var auth = await IsAuthorizedAsync(req.userId, req.Action, req.Context?.ResourceType ?? null, numericResourceId, ct).ConfigureAwait(false);
            return MapToPermissionEvaluationResult(auth);
        }

        public async Task<PermissionEvaluationResult> EvaluateAsync(PermissionEvalRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            var result = await IsAuthorizedAsync(req, ct).ConfigureAwait(false);

            // Audit the evaluation (best-effort)
            try
            {
                await _audit.RecordAuthorizationAuditAsync(req.userId, "auth.perm.eval", result.Allowed ? "allow" : "deny", result.Reason ?? string.Empty, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuthService: failed to record authorization audit for user {UserId}", req.userId);
            }

            return result;
        }

        private static AuthService authservice = new AuthService();
        public async static Task<PermissionEvaluationResult> GetIsAuthorAsync(PermissionEvalRequest req, CancellationToken ct = default)
        {            
            return await authservice.IsAuthorizedAsync(req, ct);
        }


        public async static Task<PermissionEvaluationResult> GetEvalAsync(PermissionEvalRequest req, CancellationToken ct = default)
        {           
            return await authservice.EvaluateAsync(req, ct);
        }

        // ------------------ helpers ------------------

        private static PermissionEvaluationResult MapToPermissionEvaluationResult(AuthorizationResult r)
        {
            var grant = r.Sources?.FirstOrDefault();
            return new PermissionEvaluationResult(r.Allowed, r.ExplainText, grant, null);
        }

        private static string CreateOpaqueToken(long userId, DateTimeOffset expires)
        {
            var payload = $"{userId}:{expires.ToUnixTimeSeconds()}:{CreateSecureRandomToken()}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }

        private static string CreateSecureRandomToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static bool VerifyPassword(string password, string storedHash, string storedSalt)
        {
            if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt)) return false;
            using var hmac = new HMACSHA256(Convert.FromBase64String(storedSalt));
            var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            var computedBase64 = Convert.ToBase64String(computed);
            return CryptographicEquals(computedBase64, storedHash);
        }

        private static bool CryptographicEquals(string a, string b)
        {
            var ab = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ab.Length != bb.Length) return false;
            var result = 0;
            for (var i = 0; i < ab.Length; i++) result |= ab[i] ^ bb[i];
            return result == 0;
        }

        // Explicit IAuthManager no-ct overload forwarding for backward compatibility    
        Task IAuthManager.RevokeAsync(long userId, bool revokeRefreshToken)
            => RevokeAsync(userId, revokeRefreshToken, CancellationToken.None);

    }

    public class AuthOptions
    {
        public int AccessTokenLifetimeSeconds { get; set; } = 300;
        public int RefreshTokenLifetimeSeconds { get; set; } = 60 * 60 * 24 * 14;
    }
}
