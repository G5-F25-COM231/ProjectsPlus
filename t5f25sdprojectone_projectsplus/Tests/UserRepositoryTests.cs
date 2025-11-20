using System;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class UserRepositoryTests
    {
        private InMemoryUserRepository CreateRepo() => new InMemoryUserRepository();

        [Fact]
        public async Task Insert_NewUser_SucceedsAndCanBeFound()
        {
            var repo = CreateRepo();
            var u = new UserEntity
            {
                Email = "alice@example.edu",
                DisplayName = "Alice",
                ProviderId = null
            };

            var inserted = await repo.InsertAsync(u);
            Assert.True(inserted.Id > 0);
            Assert.Equal(1, inserted.Version);
            Assert.False(inserted.IsDeleted);

            var fetched = await repo.FindByIdAsync(inserted.Id);
            Assert.NotNull(fetched);
            Assert.Equal("alice@example.edu", fetched.Email);
            Assert.Equal(inserted.Id, fetched.Id);
        }

        [Fact]
        public async Task Insert_DuplicateEmail_ThrowsDomainConflictException()
        {
            var repo = CreateRepo();
            var u1 = new UserEntity { Email = "dup@example.edu", DisplayName = "First" };
            var u2 = new UserEntity { Email = "dup@example.edu", DisplayName = "Second" };

            var saved = await repo.InsertAsync(u1);
            Assert.NotNull(saved);

            await Assert.ThrowsAsync<DomainConflictException>(async () => await repo.InsertAsync(u2));
        }

        [Fact]
        public async Task Update_WithStaleVersion_ThrowsDomainConcurrencyException()
        {
            var repo = CreateRepo();
            var u = new UserEntity { Email = "concur@example.edu", DisplayName = "Original" };
            var saved = await repo.InsertAsync(u);

            // Simulate two concurrent readers
            var copyA = await repo.FindByIdAsync(saved.Id);
            var copyB = await repo.FindByIdAsync(saved.Id);

            // update A succeeds
            copyA.DisplayName = "A-updated";
            var updatedA = await repo.UpdateAsync(copyA);
            Assert.Equal(2, updatedA.Version);

            // update B still has stale Version == 1
            copyB.DisplayName = "B-updated";
            await Assert.ThrowsAsync<DomainConcurrencyException>(async () => await repo.UpdateAsync(copyB));
        }

        [Fact]
        public async Task Delete_WithCorrectVersion_MarksIsDeleted()
        {
            var repo = CreateRepo();
            var u = new UserEntity { Email = "todelete@example.edu", DisplayName = "ToDelete" };
            var saved = await repo.InsertAsync(u);

            // delete with expected version
            await repo.DeleteAsync(saved.Id, saved.Version);

            var fetched = await repo.FindByIdAsync(saved.Id);
            Assert.Null(fetched); // FindByIdAsync returns null for deleted users in the in-memory impl
        }

        [Fact]
        public async Task Delete_WithStaleVersion_ThrowsDomainConcurrencyException()
        {
            var repo = CreateRepo();
            var u = new UserEntity { Email = "stale-delete@example.edu", DisplayName = "StaleDelete" };
            var saved = await repo.InsertAsync(u);

            // update once to increment version
            saved.DisplayName = "updated";
            var updated = await repo.UpdateAsync(saved);

            // attempt delete using old version (1)
            await Assert.ThrowsAsync<DomainConcurrencyException>(async () => await repo.DeleteAsync(saved.Id, expectedVersion: 1));
        }
    }
}
