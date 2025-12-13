// Tests/Boards/KanbanBoardTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace.KanbanBoard;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class KanbanBoardTests
    {
        [Fact]
        public void ToString_IncludesScopeSlugAndVersion()
        {
            var b = new KanbanBoard { Id = 2, WorkspaceId = 5, ProjectId = 7, Slug = "planning", Version = 3 };
            Assert.Equal("KanbanBoard:2:W:5:P:7:S:planning:v3", b.ToString());
        }

        [Fact]
        public void Defaults_NotArchived_VersionOne_Timestamps()
        {
            var now = DateTimeOffset.UtcNow;
            var b = new KanbanBoard { WorkspaceId = 5, Name = "Planning", Slug = "planning" };
            Assert.False(b.IsArchived);
            Assert.Equal(1, b.Version);
            Assert.InRange(b.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
        }
    }
}
