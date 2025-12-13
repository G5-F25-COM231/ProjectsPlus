// src/Services/Auth/IAuthManager.cs
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.DTOs.UserDTOs;

namespace t5f25sdprojectone_projectsplus.Services.Interfaces
{
    /// <summary>
    /// Minimal token response returned by AuthService and API endpoints.
    /// </summary>
    public sealed record TokenResponse(string AccessToken, string RefreshToken, int ExpiresInSeconds);

    /// <summary>
    /// Authentication manager contract used by controllers and other services.
    /// Implementations must be side-effect aware (token issuance, refresh, revocation).
    /// </summary>
    public interface IAuthManager
    {
        Task<(bool Succeeded, long UserId, string? Reason)> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
        Task<TokenResponse> IssueTokensAsync(long userId, CancellationToken ct = default);
        Task<(bool Succeeded, long UserId, string? Reason, TokenResponse? Tokens)> RefreshAsync(string refreshToken, CancellationToken ct = default);
        Task RevokeAsync(long userId, bool revokeRefreshToken, CancellationToken ct = default);
        Task<UserProfile> GetProfileAsync(long userId, CancellationToken ct = default);
        Task RevokeAsync(long userId, bool revokeRefreshToken);
    }

    
}

