// Tests/Workspace/WorkspaceTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class WorkspaceTests
    {
        [Fact]
        public void ToString_IncludesIdSlugOwnerAndVersion()
        {
            var w = new Workspace { Id = 9, Slug = "acme", OwnerUserId = 11, Version = 4 };
            Assert.Equal("Workspace:9:S:acme:Owner:11:v4", w.ToString());
        }

        [Fact]
        public void Defaults_DefaultVisibilityPrivate_VersionOne()
        {
            var w = new Workspace { Name = "Acme", Slug = "acme", OwnerUserId = 11 };
            Assert.Equal(0, w.DefaultVisibility);
            Assert.Equal(1, w.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(w.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
        }
    }
}
