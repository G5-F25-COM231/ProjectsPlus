// Repositories/IGitHubUserLinkRepository.cs
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.OLD.Identity;

namespace t5f25sdprojectone_projectsplus.Repositories.OLDR.IOLD
{
    /// <summary>
    /// Minimal repository contract for GitHubUserLink persistence and lookup.
    /// - Implementations responsible for UpdatedAt and Version bumping.
    /// - Update methods should be concurrency-aware and throw on stale version.
    /// </summary>
    public interface IGitHubUserLinkRepository
    {
        Task<GitHubUserLink?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<GitHubUserLink?> FindByGitHubUserIdAsync(long githubUserId, CancellationToken ct = default);
        Task<GitHubUserLink?> FindByLocalUserIdAsync(long localUserId, CancellationToken ct = default);
        Task<GitHubUserLink> CreateAsync(GitHubUserLink link, CancellationToken ct = default);
        Task<GitHubUserLink> UpdateAsync(GitHubUserLink link, CancellationToken ct = default);
        Task DeleteAsync(long id, CancellationToken ct = default);
    }
}
