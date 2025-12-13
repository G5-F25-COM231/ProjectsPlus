// Tests/Project/ProjectMemberTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Project;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.ProjectTests
{
    public class ProjectMemberTests
    {
        [Fact]
        public void ToString_IncludesIdsRoleAndVersion()
        {
            var m = new ProjectMember { Id = 101, ProjectId = 33, UserId = 77, Role = 2, Version = 3 };
            Assert.Equal("ProjectMember:101:P:33:U:77:R:2:v3", m.ToString());
        }

        [Fact]
        public void Defaults_TimestampsAndVersion()
        {
            var m = new ProjectMember { ProjectId = 33, UserId = 77, Role = 2 };
            Assert.Equal(1, m.Version);
            var now = DateTimeOffset.UtcNow;
            Assert.InRange(m.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.Null(m.RemovedAt);
        }
    }
}
