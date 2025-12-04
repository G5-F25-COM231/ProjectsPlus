// src/ProjectsPlus.Comms/Controllers/TemplatesAdminController.cs
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("api/admin/templates")]
    public class TemplatesAdminController : ControllerBase
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<TemplatesAdminController> _logger;

        public TemplatesAdminController(ProjectsPlusDbContext db, ILogger<TemplatesAdminController> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var t = await _db.Templates.FirstOrDefaultAsync(x => x.TemplateId == id.ToString()).ConfigureAwait(false);
            if (t == null) return NotFound();
            return Ok(new
            {
                t.TemplateId,
                t.Name,
                t.Channel,
                t.SubjectTemplate,
                t.BodyTemplate,
                t.IsActive,
                t.CreatedAt,
                t.UpdatedAt
            });
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] TemplateCreateDto dto)
        {
            if (dto == null) return BadRequest();

            var entity = new TemplateEntity
            {
                TemplateId = Guid.NewGuid().ToString(),
                Name = dto.Name,
                Channel = dto.Channel,
                SubjectTemplate = dto.Subject,
                BodyTemplate = dto.Body,
                IsActive = dto.IsActive,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Templates.Add(entity);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return CreatedAtAction(nameof(Get), new { id = entity.TemplateId }, new { entity.TemplateId });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] TemplateUpdateDto dto)
        {
            var t = await _db.Templates.FirstOrDefaultAsync(x => x.TemplateId == id.ToString()).ConfigureAwait(false);
            if (t == null) return NotFound();

            t.Name = dto.Name ?? t.Name;
            t.SubjectTemplate = dto.Subject ?? t.SubjectTemplate;
            t.BodyTemplate = dto.Body ?? t.BodyTemplate;
            t.IsActive = dto.IsActive ?? t.IsActive;
            t.UpdatedAt = DateTime.UtcNow;

            _db.Templates.Update(t);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var t = await _db.Templates.FirstOrDefaultAsync(x => x.TemplateId == id.ToString()).ConfigureAwait(false);
            if (t == null) return NotFound();

            _db.Templates.Remove(t);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return NoContent();
        }
    }

    public class TemplateCreateDto
    {
        public string Name { get; set; } = null!;
        public string Channel { get; set; } = null!;
        public string? Subject { get; set; }
        public string Body { get; set; } = null!;
        public bool IsActive { get; set; } = true;
    }

    public class TemplateUpdateDto
    {
        public string? Name { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public bool? IsActive { get; set; }
    }
}
