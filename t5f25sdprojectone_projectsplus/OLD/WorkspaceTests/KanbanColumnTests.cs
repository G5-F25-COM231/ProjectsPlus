// Tests/Boards/KanbanColumnTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace.KanbanBoard;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class KanbanColumnTests
    {
        [Fact]
        public void ToString_IncludesBoardSlugPositionAndVersion()
        {
            var c = new KanbanColumn { Id = 11, BoardId = 3, Slug = "todo", Position = 0, Version = 2 };
            Assert.Equal("KanbanColumn:11:B:3:S:todo:Pos:0:v2", c.ToString());
        }

        [Fact]
        public void Defaults_PositionZero_NotArchived_Timestamps()
        {
            var now = DateTimeOffset.UtcNow;
            var c = new KanbanColumn { BoardId = 3, Name = "To Do", Slug = "todo" };
            Assert.Equal(0, c.Position);
            Assert.False(c.IsArchived);
            Assert.Equal(1, c.Version);
            Assert.InRange(c.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.NotNull(c.Cards);
        }
    }
}
