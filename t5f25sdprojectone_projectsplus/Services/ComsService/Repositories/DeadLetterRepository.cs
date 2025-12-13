// src/ProjectsPlus.Comms/Persistence/DeadLetterRepository.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService; // for PagedResult<T>, NotificationDto, ChannelType
using t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    public class DeadLetterRepository : IDeadLetterRepository
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly INotificationRepository _notificationRepo;

        public DeadLetterRepository(ProjectsPlusDbContext db, INotificationRepository notificationRepo)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _notificationRepo = notificationRepo ?? throw new ArgumentNullException(nameof(notificationRepo));
        }

        public async Task AddAsync(DeadLetterEntity deadLetter, CancellationToken ct = default)
        {
            if (deadLetter == null) throw new ArgumentNullException(nameof(deadLetter));
            await _db.DeadLetters.AddAsync(deadLetter, ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<DeadLetterEntity?> GetAsync(Guid deadId, CancellationToken ct = default)
        {
            return await _db.DeadLetters
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DeadId == deadId, ct)
                .ConfigureAwait(false);
        }

        public async Task<PagedResult<DeadLetterEntity>> QueryAsync(DeadLetterQuery query, CancellationToken ct = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            var q = _db.DeadLetters.AsQueryable();

            if (query.NotificationId.HasValue) q = q.Where(d => d.NotificationId == query.NotificationId.Value);
            if (!string.IsNullOrWhiteSpace(query.Channel))
            {
                // Channel is stored as object on the entity; attempt to filter by string representation.
                q = q.Where(d => EF.Functions.Like(EF.Property<string>(d, "Channel"), $"%{query.Channel}%"));
            }
            if (query.FromUtc.HasValue) q = q.Where(d => d.CreatedAt >= query.FromUtc.Value);
            if (query.ToUtc.HasValue) q = q.Where(d => d.CreatedAt <= query.ToUtc.Value);

            var total = await q.CountAsync(ct).ConfigureAwait(false);

            var items = await q
                .OrderByDescending(d => d.CreatedAt)
                .Skip(query.PageSize * (Math.Max(1, query.Page) - 1))
                .Take(query.PageSize)
                .AsNoTracking()
                .ToListAsync(ct)
                .ConfigureAwait(false);

            // Map to the project's canonical PagedResult<T> (Items, ContinuationToken, TotalCount)
            var result = new PagedResult<DeadLetterEntity>
            {
                Items = items,
                ContinuationToken = null,
                TotalCount = total
            };

            return result;
        }

        public async Task DeleteAsync(Guid deadId, CancellationToken ct = default)
        {
            var row = await _db.DeadLetters.FirstOrDefaultAsync(d => d.DeadId == deadId, ct).ConfigureAwait(false);
            if (row == null) return;
            _db.DeadLetters.Remove(row);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        public async Task<Guid> RequeueAsync(Guid deadId, CancellationToken ct = default)
        {
            // Load dead letter
            var row = await _db.DeadLetters.FirstOrDefaultAsync(d => d.DeadId == deadId, ct).ConfigureAwait(false);
            if (row == null) return Guid.Empty;

            // Build a NotificationDto from the dead letter. Avoid assuming NotificationEntity shape.
            var channelStr = row.Channel?.ToString() ?? "Unknown";
            var recipientStr = row.Recipient?.ToString() ?? string.Empty;

            var notificationDto = new NotificationDto
            {
                NotificationId = Guid.NewGuid(),
                Channel = ParseChannel(channelStr),
                Recipient = recipientStr,
                Subject = null,
                Body = row.PayloadJson ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                Priority = Priority.Normal,
                CorrelationId = row.NotificationId?.ToString()
            };

            // Use the INotificationRepository to enqueue/create the notification (project-defined contract).
            try
            {
                await _notificationRepo.EnqueueNotificationAsync(notificationDto, ct).ConfigureAwait(false);

                // Remove dead letter after successful enqueue
                _db.DeadLetters.Remove(row);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);

                return notificationDto.NotificationId;
            }
            catch
            {
                // On failure, do not remove dead letter; return Guid.Empty to indicate failure.
                return Guid.Empty;
            }
        }

        private static ChannelType ParseChannel(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return ChannelType.InApp;
            if (Enum.TryParse<ChannelType>(channel, true, out var c)) return c;
            // fallback heuristics
            var lower = channel.ToLowerInvariant();
            if (lower.Contains("email")) return ChannelType.Email;
            if (lower.Contains("sms") || lower.Contains("text")) return ChannelType.Sms;
            if (lower.Contains("push")) return ChannelType.Push;
            if (lower.Contains("webhook") || lower.Contains("http")) return ChannelType.Webhook;
            return ChannelType.InApp;
        }
    }
}
