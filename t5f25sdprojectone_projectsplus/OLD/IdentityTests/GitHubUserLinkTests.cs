// Tests/Identity/GitHubUserLinkTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Identity;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.IdentityTests
{
    public class GitHubUserLinkTests
    {
        [Fact]
        public void ToString_IncludesIdGitHubIdLoginAndVersion()
        {
            var g = new GitHubUserLink { Id = 7, GitHubUserId = 12345, Login = "octocat", Version = 2 };
            Assert.Equal("GitHubUserLink:7:G:12345:L:octocat:v2", g.ToString());
        }

        [Fact]
        public void DefaultValues_VersionOne_CreatedAtSet()
        {
            var g = new GitHubUserLink { GitHubUserId = 1, Login = "x" };
            Assert.Equal(1, g.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(g.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
        }
    }
}
