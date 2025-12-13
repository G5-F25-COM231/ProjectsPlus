// src/ProjectsPlus.Comms/Controllers/AdminCommAuditController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Controllers
{
    [ApiController]
    [Route("api/admin/comm-audit")]
    public class AdminCommAuditController : ControllerBase
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<AdminCommAuditController> _logger;

        public AdminCommAuditController(ProjectsPlusDbContext db, ILogger<AdminCommAuditController> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Query audit rows with simple filters and paging.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Query([FromQuery] AuditQueryParams q)
        {
            var query = _db.CommAudit.AsQueryable();

            if (q.NotificationId.HasValue) query = query.Where(a => a.NotificationId == q.NotificationId.Value);
            if (!string.IsNullOrWhiteSpace(q.Channel)) query = query.Where(a => a.Channel == q.Channel);
            if (!string.IsNullOrWhiteSpace(q.Recipient)) query = query.Where(a => a.Recipient == q.Recipient);
            if (!string.IsNullOrWhiteSpace(q.EventType)) query = query.Where(a => a.EventType == q.EventType);

            if (q.FromUtc.HasValue) query = query.Where(a => a.Timestamp >= q.FromUtc.Value);
            if (q.ToUtc.HasValue) query = query.Where(a => a.Timestamp <= q.ToUtc.Value);

            var total = await query.CountAsync().ConfigureAwait(false);

            var items = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip(q.PageSize * (q.Page - 1))
                .Take(q.PageSize)
                .Select(a => new
                {
                    a.AuditId,
                    a.NotificationId,
                    a.Channel,
                    a.Recipient,
                    a.EventType,
                    a.Timestamp,
                    a.CorrelationId
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return Ok(new { total, page = q.Page, pageSize = q.PageSize, items });
        }

        /// <summary>
        /// Get a single audit row payload.
        /// </summary>
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var row = await _db.CommAudit.FirstOrDefaultAsync(a => a.AuditId == id).ConfigureAwait(false);
            if (row == null) return NotFound();
            return Ok(new
            {
                row.AuditId,
                row.NotificationId,
                row.Channel,
                row.Recipient,
                row.EventType,
                row.Timestamp,
                row.PayloadJson,
                row.CorrelationId
            });
        }
    }

    //---------------------------------------------------/////////////////////////

    [ApiController]
    [Route("api/admin/dead-letters")]
    public class DeadLettersController : ControllerBase
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<DeadLettersController> _logger;

        public DeadLettersController(ProjectsPlusDbContext db, ILogger<DeadLettersController> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DeadLetterQueryParams q)
        {
            var query = _db.DeadLetters.AsQueryable();

            if (q.NotificationId.HasValue) query = query.Where(d => d.NotificationId == q.NotificationId.Value);
            if (!string.IsNullOrWhiteSpace(q.Channel)) query = query.Where(d => d.Channel == q.Channel);
            if (q.FromUtc.HasValue) query = query.Where(d => d.CreatedAt >= q.FromUtc.Value);
            if (q.ToUtc.HasValue) query = query.Where(d => d.CreatedAt <= q.ToUtc.Value);

            var total = await query.CountAsync().ConfigureAwait(false);

            var items = await query
                .OrderByDescending(d => d.CreatedAt)
                .Skip(q.PageSize * (q.Page - 1))
                .Take(q.PageSize)
                .Select(d => new
                {
                    d.DeadId,
                    d.NotificationId,
                    d.Channel,
                    d.Recipient,
                    d.Error,
                    d.CreatedAt
                })
                .ToListAsync()
                .ConfigureAwait(false);

            return Ok(new { total, page = q.Page, pageSize = q.PageSize, items });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var row = await _db.DeadLetters.FirstOrDefaultAsync(d => d.DeadId == id).ConfigureAwait(false);
            if (row == null) return NotFound();
            return Ok(row);
        }

        [HttpPost("{id:guid}/requeue")]
        public async Task<IActionResult> Requeue(Guid id)
        {
            var row = await _db.DeadLetters.FirstOrDefaultAsync(d => d.DeadId == id).ConfigureAwait(false);            
            if (row == null) return NotFound();

            // Simple requeue: create a new Notification from dead letter payload
            var notif = new NotificationEntity
            {
                NotificationId = Guid.NewGuid(),
                Channel = (string)row.Channel,
                Recipient = (string)row.Recipient,
                Body = row.PayloadJson,
                Status = "Pending",
                Attempts = 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.Notifications.Add(notif);
            _db.DeadLetters.Remove(row);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return Accepted(new { requeuedNotificationId = notif.NotificationId });
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var row = await _db.DeadLetters.FirstOrDefaultAsync(d => d.DeadId == id).ConfigureAwait(false);
            if (row == null) return NotFound();

            _db.DeadLetters.Remove(row);
            await _db.SaveChangesAsync().ConfigureAwait(false);

            return NoContent();
        }
    }
    //---------------------------------------------------/////////////////////////

    public class DeadLetterQueryParams
    {
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public class AuditQueryParams
    {
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public string? Recipient { get; set; }
        public string? EventType { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }
}
