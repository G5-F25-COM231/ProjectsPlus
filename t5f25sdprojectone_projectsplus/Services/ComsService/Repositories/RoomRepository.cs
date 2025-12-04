// src/ProjectsPlus.Comms/Persistence/RoomRepository.Ef.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using t5f25sdprojectone_projectsplus.Data;
using t5f25sdprojectone_projectsplus.Models.Communication;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using static t5f25sdprojectone_projectsplus.Services.ComsService.Repositories.MessageRepository;


namespace t5f25sdprojectone_projectsplus.Services.ComsService.Repositories
{
    // If you truly don't have IRoomRepository defined in your Comms project,
    // uncomment the interface below. If you already have it, keep it commented out.
    
    public interface IRoomRepository
    {
        Task<RoomDto> CreateRoomAsync(CreateRoomRequest req, CancellationToken ct = default);
        Task<RoomDto?> GetRoomAsync(Guid roomId, CancellationToken ct = default);
        Task<IReadOnlyList<RoomDto>> GetWorkspaceRoomsAsync(long workspaceId, CancellationToken ct = default);
        Task AddMemberAsync(Guid roomId, long userId, string role = "member", CancellationToken ct = default);
        Task RemoveMemberAsync(Guid roomId, long userId, CancellationToken ct = default);
        Task<IReadOnlyList<RoomMemberDto>> GetRoomMembersAsync(Guid roomId, CancellationToken ct = default);
        Task UpdateRoomMetadataAsync(Guid roomId, IDictionary<string, object?>? metadata, CancellationToken ct = default);
        Task DeleteRoomAsync(Guid roomId, CancellationToken ct = default);
    }
    

    public class RoomRepositoryEf : IRoomRepository
    {
        private readonly ProjectsPlusDbContext _db;

        public RoomRepositoryEf(ProjectsPlusDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<RoomDto> CreateRoomAsync(CreateRoomRequest req, CancellationToken ct = default)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            var now = DateTime.UtcNow;

            var entity = new RoomEntity
            {
                RoomId = req.RoomId == Guid.Empty ? Guid.NewGuid() : req.RoomId,
                WorkspaceId = ConvertLongToGuidNullable(req.WorkspaceId),
                Name = req.Name ?? string.Empty,
                IsPrivate = req.IsPrivate,
                MetadataJson = req.Metadata == null ? null : JsonSerializer.Serialize(req.Metadata),
                CreatedAt = req.CreatedAt == default ? now : req.CreatedAt
            };

            await _db.Rooms!.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);

            // Optionally add initial members
            if (req.InitialMemberUserIds != null && req.InitialMemberUserIds.Count > 0)
            {
                var members = req.InitialMemberUserIds.Select(uid => new RoomMemberEntity
                {
                    RoomId = entity.RoomId,
                    UserId = uid,
                    Role = "member",
                    JoinedAt = now
                }).ToList();

                await _db.RoomMembers!.AddRangeAsync(members, ct);
                await _db.SaveChangesAsync(ct);
            }

            return MapToDto(entity);
        }

        public async Task<RoomDto?> GetRoomAsync(Guid roomId, CancellationToken ct = default)
        {
            var e = await _db.Rooms!.AsNoTracking().FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
            return e == null ? null : MapToDto(e);
        }

        public async Task<IReadOnlyList<RoomDto>> GetWorkspaceRoomsAsync(long workspaceId, CancellationToken ct = default)
        {
            var items = await _db.Rooms!.AsNoTracking()
                .Where(r => r.WorkspaceId == ConvertLongToGuid(workspaceId))
                .OrderBy(r => r.Name)
                .ToListAsync(ct);

            return items.Select(MapToDto).ToList();
        }

        public async Task AddMemberAsync(Guid roomId, long userId, string role = "member", CancellationToken ct = default)
        {
            var exists = await _db.RoomMembers!.FindAsync(new object[] { roomId, userId }, ct);
            if (exists != null) return;

            var entity = new RoomMemberEntity
            {
                RoomId = roomId,
                UserId = userId,
                Role = string.IsNullOrWhiteSpace(role) ? "member" : role,
                JoinedAt = DateTime.UtcNow
            };

            await _db.RoomMembers.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);
        }

        public async Task RemoveMemberAsync(Guid roomId, long userId, CancellationToken ct = default)
        {
            var entity = await _db.RoomMembers!.FindAsync(new object[] { roomId, userId }, ct);
            if (entity == null) return;

            _db.RoomMembers.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<RoomMemberDto>> GetRoomMembersAsync(Guid roomId, CancellationToken ct = default)
        {
            var items = await _db.RoomMembers!.AsNoTracking()
                .Where(m => m.RoomId == roomId)
                .OrderBy(m => m.JoinedAt)
                .ToListAsync(ct);

            return items.Select(m => new RoomMemberDto
            {
                RoomId = m.RoomId,
                UserId = ConvertGuidToLong(m.UserId),
                Role = m.Role,
                JoinedAt = m.JoinedAt
            }).ToList();
        }

        public async Task UpdateRoomMetadataAsync(Guid roomId, IDictionary<string, object?>? metadata, CancellationToken ct = default)
        {
            var entity = await _db.Rooms!.FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
            if (entity == null) throw new InvalidOperationException("Room not found");

            entity.MetadataJson = metadata == null ? null : JsonSerializer.Serialize(metadata);
            _db.Rooms.Update(entity);
            await _db.SaveChangesAsync(ct);
        }

        public async Task DeleteRoomAsync(Guid roomId, CancellationToken ct = default)
        {
            var entity = await _db.Rooms!.FirstOrDefaultAsync(r => r.RoomId == roomId, ct);
            if (entity == null) return;

            // cascade delete of members is configured in EF; remove room to delete members
            _db.Rooms.Remove(entity);
            await _db.SaveChangesAsync(ct);
        }

        #region Mapping helpers

        private static RoomDto MapToDto(RoomEntity e)
        {
            IDictionary<string, object?>? meta = null;
            if (!string.IsNullOrWhiteSpace(e.MetadataJson))
            {
                try { meta = JsonSerializer.Deserialize<Dictionary<string, object?>>(e.MetadataJson); }
                catch { meta = null; }
            }

            return new RoomDto
            {
                RoomId = e.RoomId,
                WorkspaceId = e.WorkspaceId,
                Name = e.Name,
                IsPrivate = e.IsPrivate,
                Metadata = meta,
                CreatedAt = e.CreatedAt
            };
        }

        #endregion
    }

    #region DTO / Request placeholders

    // Adjusted types to match your EF entities:
    // - WorkspaceId and UserId are long (matching WorkspaceEntity.Id and UserEntity.Id)
    // - RoomId remains Guid
    // - Metadata uses IDictionary<string, object?> to allow mixed value types

    public sealed class RoomMemberDto
    {
        public Guid RoomId { get; set; }
        public long UserId { get; set; }
        public string Role { get; set; } = "member";
        public DateTime JoinedAt { get; set; }
    }

    public sealed class CreateRoomRequest
    {
        public Guid RoomId { get; set; }
        public long? WorkspaceId { get; set; }
        public string? Name { get; set; }
        public bool IsPrivate { get; set; } = true;
        public IDictionary<string, object?>? Metadata { get; set; }
        public List<long>? InitialMemberUserIds { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    #endregion
}
