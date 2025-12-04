// src/ProjectsPlus.Comms/Persistence/CommAuditStore.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Services.ComsService.Repositories;

namespace t5f25sdprojectone_projectsplus.Services.ComsService
{
    /// <summary>
    /// EF-backed implementation of ICommAuditStore.
    /// Stores CommAuditEntity rows and exposes query/record operations.
    /// Uses a simple continuation token based on TimestampUtc.Ticks and CommAuditId.
    /// Token format: base64("{ticks}:{commAuditId}")
    /// </summary>
    public class CommAuditStore : ICommAuditStore
    {
        private readonly ProjectsPlusDbContext _db;
        private readonly ILogger<CommAuditStore> _logger;

        public CommAuditStore(IServiceProvider svc, ILogger<CommAuditStore> logger)
        {
            using var scope = svc.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ProjectsPlusDbContext>();
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task RecordAsync(CommunicationAuditEntryDto entry, CancellationToken ct = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            var entity = new CommAuditEntity
            {
                AuditId = entry.AuditId == Guid.Empty ? Guid.NewGuid() : entry.AuditId,
                NotificationId = entry.NotificationId == Guid.Empty ? null : entry.NotificationId,
                Channel = entry.Channel.ToString(),
                Recipient = entry.Recipient,
                EventType = entry.Status.ToString(),
                Timestamp = entry.Timestamp == default ? DateTime.UtcNow : entry.Timestamp,
                PayloadJson = entry.PayloadJson,
                CorrelationId = entry.Metadata != null && entry.Metadata.TryGetValue("correlationId", out var corr) ? corr : null,
                ProviderMessageId = entry.ProviderMessageId,
                ErrorMessage = entry.ErrorMessage
            };

            try
            {
                await _db.CommAudit.AddAsync(entity, ct).ConfigureAwait(false);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist comm audit entry {AuditId}", entity.AuditId);
                throw;
            }
        }

        public async Task<PagedResult<CommunicationAuditEntryDto>> QueryAsync(Guid? notificationId = null, int pageSize = 50, string? continuationToken = null, CancellationToken ct = default)
        {
            if (pageSize <= 0) pageSize = 50;

            var q = _db.CommAudit.AsNoTracking().AsQueryable();

            if (notificationId.HasValue)
            {
                q = q.Where(a => a.NotificationId == notificationId.Value);
            }

            // Order by newest first, tie-break by CommAuditId for deterministic paging
            q = q.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.AuditId);

            // Apply continuation token if present
            if (!string.IsNullOrWhiteSpace(continuationToken))
            {
                if (TryDecodeContinuationToken(continuationToken, out var ticks, out var lastId))
                {
                    var lastTs = new DateTime(ticks, DateTimeKind.Utc);
                    // For descending order, take items strictly older than the token position,
                    // or equal timestamp but with CommAuditId less than lastId (string compare).
                    q = q.Where(a =>
                        a.Timestamp < lastTs
                        || a.Timestamp == lastTs && string.Compare(a.AuditId.ToString(), lastId, StringComparison.Ordinal) < 0);
                }
                else
                {
                    // invalid token: ignore it (could alternatively throw)
                }
            }

            var total = await q.CountAsync(ct).ConfigureAwait(false);

            var rows = await q
                .Take(pageSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var items = rows.Select(r => new CommunicationAuditEntryDto
            {
                AuditId = r.AuditId,
                NotificationId = r.NotificationId ?? Guid.Empty,
                Channel = ParseChannel(r.Channel),
                Recipient = r.Recipient ?? string.Empty,
                Status = ParseStatus(r.EventType),
                ProviderMessageId = r.ProviderMessageId,
                ErrorMessage = r.ErrorMessage,
                Timestamp = r.Timestamp,
                PayloadJson = r.PayloadJson,
                Metadata = r.CorrelationId != null ? new Dictionary<string, string> { { "correlationId", r.CorrelationId } } : null
            }).ToList();

            // Build continuation token from last item if there are more items
            string? nextToken = null;
            if (rows.Count == pageSize)
            {
                var last = rows.Last();
                nextToken = CreateContinuationToken(last.Timestamp, last.AuditId.ToString());
            }

            return new PagedResult<CommunicationAuditEntryDto>
            {
                Items = items,
                ContinuationToken = nextToken,
                TotalCount = total
            };
        }

        #region Helpers

        private static string CreateContinuationToken(DateTime tsUtc, string id)
        {
            var payload = $"{tsUtc.ToUniversalTime().Ticks}:{id}";
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        }

        private static bool TryDecodeContinuationToken(string token, out long ticks, out string id)
        {
            ticks = 0;
            id = string.Empty;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var parts = decoded.Split(':', 2);
                if (parts.Length != 2) return false;
                if (!long.TryParse(parts[0], out ticks)) return false;
                id = parts[1];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ChannelType ParseChannel(string? channel)
        {
            if (string.IsNullOrWhiteSpace(channel)) return ChannelType.InApp;
            if (Enum.TryParse<ChannelType>(channel, true, out var c)) return c;
            var lower = channel.ToLowerInvariant();
            if (lower.Contains("email")) return ChannelType.Email;
            if (lower.Contains("sms") || lower.Contains("text")) return ChannelType.Sms;
            if (lower.Contains("push")) return ChannelType.Push;
            if (lower.Contains("webhook") || lower.Contains("http")) return ChannelType.Webhook;
            return ChannelType.InApp;
        }

        private static SendStatus ParseStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return SendStatus.Pending;
            if (Enum.TryParse<SendStatus>(status, true, out var s)) return s;
            var lower = status.ToLowerInvariant();
            if (lower.Contains("deliv")) return SendStatus.Delivered;
            if (lower.Contains("fail") || lower.Contains("bounce")) return SendStatus.Failed;
            if (lower.Contains("sent")) return SendStatus.Sent;
            return SendStatus.Pending;
        }

        #endregion
    }

    // Minimal CommAuditEntity shape expected by this store.
    // If your project already defines this entity, remove this class.
    //public class CommAuditEntity
    //{
    //    public Guid CommAuditId { get; set; }
    //    public Guid? NotificationId { get; set; }
    //    public string? Channel { get; set; }
    //    public string? Recipient { get; set; }
    //    public string? EventType { get; set; }
    //    public DateTime TimestampUtc { get; set; }
    //    public string? PayloadJson { get; set; }
    //    public string? CorrelationId { get; set; }
    //    public string? ProviderMessageId { get; set; }
    //    public string? ErrorMessage { get; set; }
    //}
}
