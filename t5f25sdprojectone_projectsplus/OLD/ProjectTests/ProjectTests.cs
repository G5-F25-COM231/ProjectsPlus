// Tests/Project/ProjectTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Project;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.ProjectTests
{
    public class ProjectTests
    {
        [Fact]
        public void ToString_IncludesWorkspaceSlugAndVersion()
        {
            var p = new Project { Id = 13, WorkspaceId = 5, Slug = "alpha", Version = 2 };
            Assert.Equal("Project:13:W:5:S:alpha:v2", p.ToString());
        }

        [Fact]
        public void Defaults_StatusVisibilityAndTimestamps()
        {
            var p = new Project { WorkspaceId = 5, Name = "Alpha", Slug = "alpha", OwnerUserId = 11 };
            Assert.Equal(0, p.Status);
            Assert.Equal(0, p.Visibility);
            Assert.Equal(1, p.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(p.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.NotNull(p.Members);
        }
    }
}
