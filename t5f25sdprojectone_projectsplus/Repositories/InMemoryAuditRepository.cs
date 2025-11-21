// test/TestHelpers/InMemoryAuditRepository.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public record AuthorizationAuditEntry(DateTimeOffset Timestamp, long? ActorUserId, string Action, string Outcome, string? Detail);

    /// <summary>
    /// Simple in-memory audit repository for component/unit tests.
    /// Stores authorization audit entries in a thread-safe collection and exposes them for assertions.
    /// </summary>
    public class InMemoryAuditRepository : IAuditRepository
    {
        private readonly ConcurrentQueue<AuthorizationAuditEntry> _queue = new();
        private readonly ProjectsPlusDbContext _db;

        public InMemoryAuditRepository(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task WriteAsync(ProjectAudit audit, CancellationToken ct = default)
        {
            if (audit == null) throw new ArgumentNullException(nameof(audit));

            // Ensure timestamp is set deterministically if caller didn't set it
            if (audit.CreatedAt == default) audit.CreatedAt = DateTimeOffset.UtcNow;

            // Persist as a new row; keep it simple and idempotent on caller side
            await _db.ProjectAudits.AddAsync(audit, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public Task RecordAuthorizationAuditAsync(long? actorUserId, string action, string outcome, string? detail = null, CancellationToken ct = default)
        {
            var entry = new AuthorizationAuditEntry(DateTimeOffset.UtcNow, actorUserId, action, outcome, detail);
            _queue.Enqueue(entry);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Retrieve all recorded entries in FIFO order for test assertions.
        /// </summary>
        public IReadOnlyList<AuthorizationAuditEntry> GetAll() => _queue.ToArray();
        public void Clear() { while (_queue.TryDequeue(out _)) { } }
    }
}
