// src/ProjectsPlus.Comms/Persistence/IDeadLetterRepository.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models.Communication;

namespace t5f25sdprojectone_projectsplus.Services.ComsService.Interfaces
{
    public class DeadLetterQuery
    {
        public Guid? NotificationId { get; set; }
        public string? Channel { get; set; }
        public DateTime? FromUtc { get; set; }
        public DateTime? ToUtc { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    public interface IDeadLetterRepository
    {
        Task AddAsync(DeadLetterEntity deadLetter, CancellationToken ct = default);
        Task<DeadLetterEntity?> GetAsync(Guid deadId, CancellationToken ct = default);
        /// <summary>
        /// Query dead letters. Returns the project's canonical PagedResult<T>.
        /// </summary>
        Task<PagedResult<DeadLetterEntity>> QueryAsync(DeadLetterQuery query, CancellationToken ct = default);
        Task DeleteAsync(Guid deadId, CancellationToken ct = default);

        /// <summary>
        /// Requeue the dead letter by creating/enqueuing a NotificationDto via INotificationRepository.
        /// Returns the new NotificationId on success, Guid.Empty on failure.
        /// </summary>
        Task<Guid> RequeueAsync(Guid deadId, CancellationToken ct = default);
    }
}
