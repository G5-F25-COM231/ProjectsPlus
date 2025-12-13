// Tests/Boards/KanbanCardTests.cs
using System;
using t5f25sdprojectone_projectsplus.Models.Workspace.KanbanBoard;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.OLD.WorkspaceTests
{
    public class KanbanCardTests
    {
        [Fact]
        public void ToString_IncludesColumnPositionTitleAndVersion()
        {
            var c = new KanbanCard { Id = 77, ColumnId = 9, Position = 3, Title = "Fix bug", Version = 2 };
            Assert.Equal("KanbanCard:77:C:9:P:3:T:Fix bug:v2", c.ToString());
        }

        [Fact]
        public void Defaults_PositionZero_NotBlocked_NotArchived_Timestamps()
        {
            var now = DateTimeOffset.UtcNow;
            var c = new KanbanCard { ColumnId = 9, Title = "New feature" };
            Assert.Equal(0, c.Position);
            Assert.False(c.IsBlocked);
            Assert.False(c.IsArchived);
            Assert.Equal(1, c.Version);
            Assert.InRange(c.CreatedAt, now.AddMinutes(-1), now.AddMinutes(1));
            Assert.NotNull(c.Attachments);
            Assert.NotNull(c.Assignees);
        }
    }
}
