// Tests/Identity/UserTests.cs
using System;
using Xunit;
using t5f25sdprojectone_projectsplus.Models.Identity;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.IdentityTests
{
    public class UserTests
    {
        [Fact]
        public void ToString_IncludesIdEmailAndVersion()
        {
            var u = new User { Id = 42, Email = "alice@example.com", Version = 3 };
            Assert.Equal("User:42:E:alice@example.com:v3", u.ToString());
        }

        [Fact]
        public void DefaultValues_VerifiedFalse_VersionOne()
        {
            var u = new User { Email = "bob@example.com" };
            Assert.False(u.Verified);
            Assert.Equal(1, u.Version);
            // Check CreatedAt roughly equals now (allow small tolerance)
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(u.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
        }

        // Concurrency test skeleton: requires concrete repository/EF in-memory provider to exercise.
        // [Fact] public async Task UpdateAsync_ThrowsOnStaleVersion() { ... }
    }
}
