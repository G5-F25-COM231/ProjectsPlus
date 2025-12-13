// DTOs/Identity/RegisterResponseAndAuthTokens.cs
using System;

namespace t5f25sdprojectone_projectsplus.OLD.DTO.Identity
{
    /// <summary>
    /// Lightweight projection of a user returned after registration actions.
    /// Must not include sensitive fields (PasswordHash, PasswordSalt, raw refresh tokens).
    /// </summary>
    public sealed class UserProjectionDto
    {
        public long Id { get; init; }
        public string Username { get; init; } = null!;
        public string Email { get; init; } = null!;
        public long? SystemTypeId { get; init; }
        public string SignupRoute { get; init; } = null!;
        public string SignupStatus { get; init; } = null!;
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    /// <summary>
    /// Response returned immediately after a registration call.
    /// - For External route, SignupStatus will typically be PendingApproval and no tokens are returned.
    /// - For Admin-seed or Admin-created flows, the service may return a message noting that a default password was emailed.
    /// </summary>
    public sealed class RegisterResponse
    {
        public UserProjectionDto? User { get; init; }
        public string SignupStatus { get; init; } = null!;
        public string Message { get; init; } = string.Empty;
    }

    /// <summary>
    /// Authentication tokens returned on successful signin or token exchange.
    /// - access_token is a short-lived token (JWT or opaque) used for Authorization header.
    /// - refresh_token is returned only on creation/exchange; callers must persist it securely.
    /// - must_change_password indicates the client should force a password-change flow on next interactive login.
    /// </summary>
    public sealed class AuthTokensDto
    {
        public string AccessToken { get; init; } = null!;
        public string TokenType { get; init; } = "Bearer";
        public int ExpiresIn { get; init; }   // seconds until access token expiry
        public string? RefreshToken { get; init; }   // raw refresh token only when issued; otherwise null
        public bool MustChangePassword { get; init; }
    }
}
