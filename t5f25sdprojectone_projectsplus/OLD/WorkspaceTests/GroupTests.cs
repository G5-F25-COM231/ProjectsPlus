// Tests/Workspace/GroupTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace.Group;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class GroupTests
    {
        [Fact]
        public void ToString_IncludesWorkspaceSlugAndVersion()
        {
            var g = new Group { Id = 3, WorkspaceId = 7, Slug = "infra", Version = 2 };
            Assert.Equal("Group:3:W:7:S:infra:v2", g.ToString());
        }

        [Fact]
        public void Defaults_TimestampsAndVersion()
        {
            var g = new Group { WorkspaceId = 1, Name = "Infra", Slug = "infra" };
            Assert.Equal(1, g.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(g.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.NotNull(g.Members);
        }
    }
}
