// src/Controllers/ProjectsController.cs
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using t5f25sdprojectone_projectsplus.Services.Interfaces;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Commons;
using t5f25sdprojectone_projectsplus.Services.Authorization;
using IAuthorizationService = t5f25sdprojectone_projectsplus.Services.Interfaces.IAuthorizationService;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("v1/projects")]
    public class ProjectsController : ControllerBase
    {
        private readonly IProjectService _projects;
        private readonly IAuthorizationService _auth;
        private readonly CorrelationOptions _corr;

        public ProjectsController(IProjectService projects, IAuthorizationService auth, CorrelationOptions corr)
        {
            _projects = projects;
            _auth = auth;
            _corr = corr;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ProjectEntity request, CancellationToken ct)
        {
            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            if (!(await _auth.IsAuthorizedAsync(0, "Project.Create", "Project", null, ct)).Allowed)
                return Forbid();

            var created = await _projects.CreateProjectAsync(request, ct).ConfigureAwait(false);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpPatch("{id:long}")]
        public async Task<IActionResult> Patch(long id, [FromBody] ProjectEntity patch, CancellationToken ct)
        {
            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            if (!(await _auth.IsAuthorizedAsync(0, "Project.Edit", "Project", id, ct)).Allowed)
                return Forbid();

            patch.Id = id;
            var updated = await _projects.UpdateProjectAsync(patch, ct).ConfigureAwait(false);
            return Ok(updated);
        }

        [HttpPost("{id:long}/submit")]
        public async Task<IActionResult> Submit(long id, [FromQuery] int expectedVersion, [FromBody] SubmitRequest body, CancellationToken ct)
        {
            if (!Request.Headers.ContainsKey(_corr.HeaderName))
                return BadRequest(new { error = "Missing correlation id", correlationHeader = _corr.HeaderName });

            if (!(await _auth.IsAuthorizedAsync(0, "Project.Submit", "Project", id, ct)).Allowed)
                return Forbid();

            var result = await _projects.SubmitProjectAsync(id, expectedVersion, body.SubmitterUserId, body.Comment, ct).ConfigureAwait(false);
            return Ok(result);
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> Get(long id, CancellationToken ct)
        {
            if (!(await _auth.IsAuthorizedAsync(0, "Project.View", "Project", id, ct)).Allowed)
                return Forbid();

            var view = await _projects.GetProjectViewAsync(id, ct).ConfigureAwait(false);
            if (view == null) return NotFound();
            return Ok(view);
        }

        public sealed class SubmitRequest
        {
            public string SubmitterUserId { get; set; } = string.Empty;
            public string? Comment { get; set; }
        }
    }
}
