// test/unit/Controllers/ProjectsControllerTests.cs
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using t5f25sdprojectone_projectsplus.Data;
using Assert = Xunit.Assert;
using t5f25sdprojectone_projectsplus.RegExtension;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class ProjectsControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public ProjectsControllerTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Replace DB with in-memory test DB
                    services.AddProjectsPlusWithInMemoryDb("projectsplus_ctrl_tests");
                });
            });
        }

        [Fact]
        public async Task PostProject_WithoutCorrelationHeader_Returns400()
        {
            var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new { title = "T" });
            var resp = await client.PostAsync("/v1/projects", new StringContent(payload, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task PostProject_WithCorrelationAndAuth_Returns201()
        {
            var client = _factory.CreateClient();
            var payload = JsonSerializer.Serialize(new { title = "T" });
            var req = new HttpRequestMessage(HttpMethod.Post, "/v1/projects")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("X-Correlation-Id", "test-corr");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
    }
}
