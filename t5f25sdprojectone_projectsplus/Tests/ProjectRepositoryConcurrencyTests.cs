using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class ProjectRepositoryConcurrencyTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ProjectsPlusDbContext> _options;

        public ProjectRepositoryConcurrencyTests()
        {
            // Create and open shared in-memory SQLite connection for relational behaviors
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<ProjectsPlusDbContext>()
                .UseSqlite(_connection)
                .Options;

            // Create schema
            using var ctx = new ProjectsPlusDbContext(_options);
            ctx.Database.EnsureCreated();
        }

        [Fact]
        public async Task Update_WithStaleVersion_ThrowsDomainConcurrencyException()
        {
            // arrange - insert initial project
            var project = new ProjectEntity
            {
                Title = "Concurrency Project",
                ShortDescription = "desc",
                OwnerUserId = 42
            };

            using (var ctx = new ProjectsPlusDbContext(_options))
            {
                var repo = new ProjectRepository(ctx);
                var inserted = await repo.CreateAsync(project);
                Assert.Equal(1, inserted.Version);

                // simulate two readers
                var readerA = await repo.GetByIdAsync(inserted.Id);
                var readerB = await repo.GetByIdAsync(inserted.Id);

                // update A succeeds
                readerA.ShortDescription = "A updated";
                var updatedA = await repo.UpdateAsync(readerA);
                Assert.Equal(2, updatedA.Version);

                // update B still has Version == 1 -> should throw
                readerB.ShortDescription = "B updated";
                await Assert.ThrowsAsync<DomainConcurrencyException>(async () =>
                {
                    using var ctx2 = new ProjectsPlusDbContext(_options);
                    var repo2 = new ProjectRepository(ctx2);
                    await repo2.UpdateAsync(readerB);
                });
            }
        }

        [Fact]
        public async Task Delete_WithCorrectVersion_MarksIsDeleted()
        {
            // arrange - insert initial project
            var project = new ProjectEntity
            {
                Title = "Delete Project",
                ShortDescription = "to delete",
                OwnerUserId = 7
            };

            long id;
            int version;
            using (var ctx = new ProjectsPlusDbContext(_options))
            {
                var repo = new ProjectRepository(ctx);
                var inserted = await repo.CreateAsync(project);
                id = inserted.Id;
                version = inserted.Version;
            }

            // act - delete with expected version
            using (var ctx = new ProjectsPlusDbContext(_options))
            {
                var repo = new ProjectRepository(ctx);
                await repo.DeleteAsync(id, version);
            }

            // assert - cannot find by id (GetByIdAsync returns null for deleted)
            using (var ctx = new ProjectsPlusDbContext(_options))
            {
                var repo = new ProjectRepository(ctx);
                var fetched = await repo.GetByIdAsync(id);
                Assert.Null(fetched);
            }
        }

        [Fact]
        public async Task ListByOwner_ReturnsInsertedProjects()
        {
            using (var ctx = new ProjectsPlusDbContext(_options))
            {
                var repo = new ProjectRepository(ctx);
                var p1 = await repo.CreateAsync(new ProjectEntity { Title = "P1", OwnerUserId = 900 });
                var p2 = await repo.CreateAsync(new ProjectEntity { Title = "P2", OwnerUserId = 900 });
                var list = await repo.ListByOwnerAsync(900, filters: (0, 10));
                Assert.Contains(list, p => p.Id == p1.Id);
                Assert.Contains(list, p => p.Id == p2.Id);
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}
