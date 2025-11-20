// Tests/Workspace/WorkspaceMemberTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class WorkspaceMemberTests
    {
        [Fact]
        public void ToString_IncludesIdsRoleAndVersion()
        {
            var m = new WorkspaceMember { Id = 5, WorkspaceId = 2, UserId = 13, Role = 1, Version = 2 };
            Assert.Equal("WorkspaceMember:5:W:2:U:13:R:1:v2", m.ToString());
        }

        [Fact]
        public void InviteFields_DefaultsAndTimestamps()
        {
            var m = new WorkspaceMember { WorkspaceId = 2, UserId = 13, Role = 1 };
            Assert.Equal(1, m.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(m.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.Null(m.RemovedAt);
            Assert.Null(m.InviteToken);
        }
    }
}
