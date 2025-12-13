// test/acceptance/SubmitProjectAcceptanceTests.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using Assert = Xunit.Assert;
using t5f25sdprojectone_projectsplus.RegExtension;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class SubmitProjectAcceptanceTests
    {
        [Fact]
        public async Task SubmitProject_AppendsStateChangeAndTransitionsProject()
        {
            var services = new ServiceCollection();
            services.AddProjectsPlusWithInMemoryDb($"pp_accept_{Guid.NewGuid()}");
            var sp = services.BuildServiceProvider();

            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();
            var projectRepo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            var stateRepo = scope.ServiceProvider.GetRequiredService<IProjectStateChangeRepository>();
            var service = scope.ServiceProvider.GetRequiredService<Services.Interfaces.IProjectService>();

            // create project
            var created = await projectRepo.CreateAsync(new ProjectEntity { Title = "accept", WorkspaceId = 1 });
            var initialVersion = created.Version;

            // submit
            var updated = await service.SubmitProjectAsync(created.Id, initialVersion, "123", "please approve");

            Assert.Equal(ProjectStatus.Submitted, updated.Status);
            Assert.Equal(initialVersion + 1, updated.Version);

            var changes = (await stateRepo.ListByProjectIdAsync(created.Id)).ToList();
            Assert.Single(changes);
            var change = changes.Single();
            Assert.Equal(created.Status.ToString(), change.FromState);
            Assert.Equal(ProjectStatus.Submitted.ToString(), change.ToState);
            Assert.Equal(123L, change.ActorUserId);
            Assert.Equal("please approve", change.Message);
        }
    }
}
