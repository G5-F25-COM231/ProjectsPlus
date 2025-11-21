using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;

namespace t5f25sdprojectone_projectsplus.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _users;
        private readonly IAuditLogger? _auditLogger;
        private readonly IAuditRepository? _auditRepository;

        // primary constructor using IAuditRepository for the tests that assert ProjectAudit writes
        public UserService(IUserRepository users, IAuditRepository auditRepository)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        }

        // alternate constructor preserving previous IAuditLogger usage (optional)
        public UserService(IUserRepository users, IAuditLogger auditLogger)
        {
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        }

        public async Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.Email)) throw new ArgumentException("Email is required", nameof(user.Email));
            if (string.IsNullOrWhiteSpace(user.DisplayName)) throw new ArgumentException("DisplayName is required", nameof(user.DisplayName));

            user.Email = user.Email.Trim();
            user.NormalizedEmail = user.Email.ToLowerInvariant();

            var created = await _users.InsertAsync(user, ct);

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = created.Id,
                    Action = "Create",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = $"Email:{created.NormalizedEmail}"
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserCreated", created.ToString(), ct);
            }

            return created;
        }

        public Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default)
            => _users.FindByIdAsync(id, ct);

        public Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<UserEntity>(null);
            return _users.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
        }

        // compatibility wrapper expected by tests/callers
        public Task<UserEntity> GetByEmailAsync(string email, CancellationToken ct = default)
            => FindByEmailAsync(email, ct);

        public async Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            return await _users.ListAsync(page, pageSize, ct);
        }

        public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.Email = user.Email?.Trim();
            user.NormalizedEmail = user.Email?.ToLowerInvariant();

            var updated = await _users.UpdateAsync(user, ct);

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = updated.Id,
                    Action = "Update",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = $"v{updated.Version}"
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserUpdated", updated.ToString(), ct);
            }

            return updated;
        }

        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            await _users.DeleteAsync(id, expectedVersion, ct);

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = id,
                    Action = "Delete",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = $"v{expectedVersion}"
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserDeleted", $"User:{id} v{expectedVersion}", ct);
            }
        }

        public async Task<UserEntity> AuthenticateAsync(string email, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;
            var normalized = email.Trim().ToLowerInvariant();
            var user = await _users.FindByEmailAsync(normalized, ct);
            if (user == null) return null;

            var verified = await _users.VerifyPasswordAsync(user.Id, password, ct);
            if (!verified) return null;

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = user.Id,
                    Action = "Authenticate",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = null
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserAuthenticated", $"User:{user.Id}", ct);
            }

            return user;
        }

        public async Task<UserEntity> AssignRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();
            var updated = await _users.AddRoleAsync(userId, cleaned, ct);

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = userId,
                    Action = "AssignRole",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = $"Role:{cleaned}"
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserRoleAssigned", $"User:{userId} Role:{cleaned}", ct);
            }

            return updated;
        }

        public async Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();
            var updated = await _users.RemoveRoleAsync(userId, cleaned, ct);

            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = userId,
                    Action = "RemoveRole",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = $"Role:{cleaned}"
                }, ct);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync("UserRoleRemoved", $"User:{userId} Role:{cleaned}", ct);
            }

            return updated;
        }
    }
}
