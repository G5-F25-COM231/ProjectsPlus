using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Repositories;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class InfralogRepositoryTests
    {
        private DbContextOptions<ProjectsPlusDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<ProjectsPlusDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        }

        [Fact]
        public async Task Insert_ValidEntry_SucceedsAndCanBeFound()
        {
            var opts = CreateOptions();
            using var ctx = new ProjectsPlusDbContext(opts);
            ctx.Database.EnsureCreated();

            var repo = new InfralogRepository(ctx);
            var entry = new InfralogEntity
            {
                CorrelationId = "corr-1",
                Category = "Provision",
                Message = "Started provisioning"
            };

            var saved = await repo.InsertAsync(entry);
            Assert.True(saved.Id > 0);
            Assert.Equal(1, saved.Version);

            var fetched = await repo.FindByIdAsync(saved.Id);
            Assert.NotNull(fetched);
            Assert.Equal("corr-1", fetched.CorrelationId);
        }

        [Fact]
        public async Task Insert_MissingRequired_ThrowsArgumentException()
        {
            var opts = CreateOptions();
            using var ctx = new ProjectsPlusDbContext(opts);
            ctx.Database.EnsureCreated();

            var repo = new InfralogRepository(ctx);

            var missingCorrelation = new InfralogEntity { Category = "C", Message = "m" };
            await Assert.ThrowsAsync<ArgumentException>(async () => await repo.InsertAsync(missingCorrelation));

            var missingCategory = new InfralogEntity { CorrelationId = "c", Message = "m" };
            await Assert.ThrowsAsync<ArgumentException>(async () => await repo.InsertAsync(missingCategory));

            var missingMessage = new InfralogEntity { CorrelationId = "c", Category = "c" };
            await Assert.ThrowsAsync<ArgumentException>(async () => await repo.InsertAsync(missingMessage));
        }

        [Fact]
        public async Task Update_WithStaleVersion_ThrowsDomainConcurrencyException()
        {
            var opts = CreateOptions();
            using var ctx = new ProjectsPlusDbContext(opts);
            ctx.Database.EnsureCreated();

            var repo = new InfralogRepository(ctx);
            var entry = new InfralogEntity
            {
                CorrelationId = "corr-2",
                Category = "Audit",
                Message = "Initial"
            };

            var saved = await repo.InsertAsync(entry);

            // simulate two readers
            var copyA = await repo.FindByIdAsync(saved.Id);
            var copyB = await repo.FindByIdAsync(saved.Id);

            // update A
            copyA.Message = "A updated";
            var updatedA = await repo.UpdateAsync(copyA);
            Assert.Equal(2, updatedA.Version);

            // update B with stale version should throw
            copyB.Message = "B updated";
            await Assert.ThrowsAsync<DomainConcurrencyException>(async () => await repo.UpdateAsync(copyB));
        }

        [Fact]
        public async Task ListByCorrelationId_ReturnsEntriesOrderedNewestFirst()
        {
            var opts = CreateOptions();
            using var ctx = new ProjectsPlusDbContext(opts);
            ctx.Database.EnsureCreated();

            var repo = new InfralogRepository(ctx);
            await repo.InsertAsync(new InfralogEntity { CorrelationId = "c3", Category = "A", Message = "m1" });
            await Task.Delay(10);
            await repo.InsertAsync(new InfralogEntity { CorrelationId = "c3", Category = "A", Message = "m2" });

            var list = (await repo.ListByCorrelationIdAsync("c3")).ToArray();
            Assert.Equal(2, list.Length);
            Assert.True(list[0].CreatedAt >= list[1].CreatedAt);
        }
    }
}
