// Tests/Workspace/GroupMemberTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace.Group;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class GroupMemberTests
    {
        [Fact]
        public void ToString_IncludesIdsRoleAndVersion()
        {
            var m = new GroupMember { Id = 21, GroupId = 4, UserId = 19, Role = 2, Version = 5 };
            Assert.Equal("GroupMember:21:G:4:U:19:R:2:v5", m.ToString());
        }

        [Fact]
        public void Defaults_TimestampsAndVersion()
        {
            var m = new GroupMember { GroupId = 4, UserId = 19, Role = 2 };
            Assert.Equal(1, m.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(m.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.Null(m.RemovedAt);
        }
    }
}
