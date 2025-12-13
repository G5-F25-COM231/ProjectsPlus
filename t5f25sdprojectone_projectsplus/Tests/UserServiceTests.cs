using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services;
using Xunit;
using Assert = Xunit.Assert;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class UserServiceTests
    {
        [Fact]
        public async Task Create_Then_GetByEmail_Returns_NormalizedData()
        {
            var repo = new Mock<IUserRepository>();
            var audit = new Mock<IAuditRepository>();
            var now = DateTimeOffset.UtcNow;

            var input = new UserEntity
            {
                Email = "  New.User@Example.EDU ",
                DisplayName = "New User"
            };

            var created = new UserEntity
            {
                Id = 42,
                Email = "New.User@Example.EDU",
                NormalizedEmail = "new.user@example.edu",
                DisplayName = "New User",
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
                IsActive = true,
                IsDeleted = false
            };

            repo.Setup(r => r.InsertAsync(It.Is<UserEntity>(u => u.NormalizedEmail == "new.user@example.edu"), It.IsAny<CancellationToken>()))
                .ReturnsAsync(created);

            repo.Setup(r => r.FindByEmailAsync("new.user@example.edu", It.IsAny<CancellationToken>()))
                .ReturnsAsync(created);

            var service = new UserService(repo.Object, audit.Object);

            var result = await service.CreateAsync(input);

            Assert.Equal("new.user@example.edu", result.NormalizedEmail);

            var fetched = await service.GetByEmailAsync("new.user@example.edu");

            Assert.Equal(result.Id, fetched.Id);
            repo.Verify(r => r.InsertAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()), Times.Once);
            audit.Verify(a => a.WriteAsync(It.Is<ProjectAudit>(p => p.Action == "Create" && p.EntityId == 42), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Create_DuplicateEmail_Throws_DomainConflictException()
        {
            var repo = new Mock<IUserRepository>();
            var audit = new Mock<IAuditRepository>();

            var input = new UserEntity { Email = "dup@example.edu", DisplayName = "Dup" };

            repo.Setup(r => r.InsertAsync(It.IsAny<UserEntity>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DomainConflictException("duplicate"));

            var service = new UserService(repo.Object, audit.Object);

            await Assert.ThrowsAsync<DomainConflictException>(() => service.CreateAsync(input));
            audit.Verify(a => a.WriteAsync(It.IsAny<ProjectAudit>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
