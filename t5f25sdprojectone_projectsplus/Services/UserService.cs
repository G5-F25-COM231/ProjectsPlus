using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.DTOs.UserDTOs;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Projects;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
using t5f25sdprojectone_projectsplus.Services.Interfaces;
using CreateUserRequest = t5f25sdprojectone_projectsplus.Services.Interfaces.CreateUserRequest;

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

        // CreateAsync from contract
        public async Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.Email)) throw new ArgumentException("Email is required", nameof(user.Email));
            if (string.IsNullOrWhiteSpace(user.DisplayName)) throw new ArgumentException("DisplayName is required", nameof(user.DisplayName));

            user.Email = user.Email.Trim();
            user.NormalizedEmail = user.Email.ToLowerInvariant();
            user.DisplayName = user.DisplayName.Trim();

            var created = await _users.InsertAsync(user, ct).ConfigureAwait(false);

            await WriteAuditAsync("Create", created.Id, $"Email:{created.NormalizedEmail}", ct).ConfigureAwait(false);

            return created;
        }

        // Compatibility alias — forward to CreateAsync
        public Task<UserEntity> CreateUserAsync(UserEntity user, CancellationToken ct = default)
            => CreateAsync(user, ct);

        // CreateFromRequestAsync — builds a UserEntity and delegates
        public async Task<CreateUserResult> CreateFromRequestAsync(CreateUserRequest request, string correlationId, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Email)) return new CreateUserResult(false, null, "Email is required");
            if (string.IsNullOrWhiteSpace(request.Password)) return new CreateUserResult(false, null, "Password is required");

            var entity = new UserEntity
            {
                Email = request.Email.Trim(),
                NormalizedEmail = request.Email.Trim().ToLowerInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email.Trim() : request.DisplayName.Trim(),
                FirstName = request.FirstName?.Trim(),
                LastName = request.LastName?.Trim()
            };

            try
            {
                var created = await CreateAsync(entity, ct).ConfigureAwait(false);
                // NOTE: password hashing/storing should be handled by repository.InsertAsync or a separate user manager
                return new CreateUserResult(true, created.Id, null);
            }
            catch (ArgumentException ex)
            {
                return new CreateUserResult(false, null, ex.Message);
            }
            catch (DomainConflictException ex)
            {
                return new CreateUserResult(false, null, ex.Message);
            }
        }

        // FindByIdAsync returns nullable per contract
        public Task<UserEntity?> FindByIdAsync(long id, CancellationToken ct = default)
            => _users.FindByIdAsync(id, ct);

        // FindByEmailAsync returns nullable per contract
        public Task<UserEntity?> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<UserEntity?>(null);
            var normalized = email.Trim().ToLowerInvariant();
            return _users.FindByEmailAsync(normalized, ct);
        }

        // Compatibility wrapper expected by tests/callers
        public Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
            => FindByEmailAsync(email, ct);

        // GetProfileAsync maps UserEntity -> UserProfile
        public async Task<UserProfile?> GetProfileAsync(long userId, CancellationToken ct = default)
        {
            var user = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false);
            if (user == null) return null;         
            return UserProfile.FromEntity(user);
        }

        public async Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            return await _users.ListAsync(page, pageSize, ct).ConfigureAwait(false);
        }

        public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            user.Email = user.Email?.Trim();
            user.NormalizedEmail = user.Email?.ToLowerInvariant();

            var updated = await _users.UpdateAsync(user, ct).ConfigureAwait(false);

            await WriteAuditAsync("Update", updated.Id, $"v{updated.Version}", ct).ConfigureAwait(false);

            return updated;
        }

        // PatchUserAsync — applies a patch request via repository and reports result
        public async Task<PatchUserResult> PatchUserAsync(long userId, PatchUserRequest patch, CancellationToken ct = default)
        {
            if (patch == null) throw new ArgumentNullException(nameof(patch));

            // Retrieve existing
            var existing = await _users.FindByIdAsync(userId, ct).ConfigureAwait(false);
            if (existing == null) return new PatchUserResult(false, false, "Not found");

            // Apply patch fields
            if (patch.DisplayName != null) existing.DisplayName = patch.DisplayName.Trim();
            if (patch.FirstName != null) existing.FirstName = patch.FirstName.Trim();
            if (patch.LastName != null) existing.LastName = patch.LastName.Trim();

            // expected version check if provided
            if (patch.ExpectedVersion.HasValue && patch.ExpectedVersion.Value != existing.Version)
            {
                return new PatchUserResult(false, true, "Version mismatch");
            }

            try
            {
                var updated = await _users.UpdateAsync(existing, ct).ConfigureAwait(false);
                await WriteAuditAsync("Patch", updated.Id, $"v{updated.Version}", ct).ConfigureAwait(false);
                return new PatchUserResult(true, false, null);
            }
            catch (DomainConcurrencyException ex)
            {
                return new PatchUserResult(false, true, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return new PatchUserResult(false, false, ex.Message);
            }
        }

        // DeleteAsync from contract
        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
        {
            await _users.DeleteAsync(id, expectedVersion, ct).ConfigureAwait(false);
            await WriteAuditAsync("Delete", id, $"v{expectedVersion}", ct).ConfigureAwait(false);
        }

        // Compatibility alias for DeleteAsync
        public Task DeleteUserAsync(long id, int expectedVersion, CancellationToken ct = default)
            => DeleteAsync(id, expectedVersion, ct);

        // AuthenticateAsync returns nullable per contract
        public async Task<UserEntity?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;
            var normalized = email.Trim().ToLowerInvariant();
            var user = await _users.FindByEmailAsync(normalized, ct).ConfigureAwait(false);
            if (user == null) return null;

            var verified = await _users.VerifyPasswordAsync(user.Id, password, ct).ConfigureAwait(false);
            if (!verified) return null;

            await WriteAuditAsync("Authenticate", user.Id, null, ct).ConfigureAwait(false);

            return user;
        }

        public async Task<UserEntity> AssignRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();
            var updated = await _users.AddRoleAsync(userId, cleaned, ct).ConfigureAwait(false);

            await WriteAuditAsync("AssignRole", userId, $"Role:{cleaned}", ct).ConfigureAwait(false);

            return updated;
        }

        public async Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();
            var updated = await _users.RemoveRoleAsync(userId, cleaned, ct).ConfigureAwait(false);

            await WriteAuditAsync("RemoveRole", userId, $"Role:{cleaned}", ct).ConfigureAwait(false);

            return updated;
        }

        // ApplyUsersAsync — domain bulk operation
        public async Task<ApplyUsersResult> ApplyUsersAsync(ApplyUsersRequest request, string correlationId, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentNullException(nameof(correlationId));

            // Example: call repository to perform bulk operation and return a summary
            var outcomes = new List<object>();
            foreach (var uid in request.UserIds)
            {
                try
                {
                    // This example simply ensures user exists and logs the check.
                    var user = await _users.FindByIdAsync(uid, ct).ConfigureAwait(false);
                    if (user == null)
                    {
                        outcomes.Add(new { UserId = uid, Status = "NotFound" });
                        continue;
                    }

                    outcomes.Add(new { UserId = uid, Status = "Ok" });
                }
                catch (Exception ex)
                {
                    outcomes.Add(new { UserId = uid, Status = "Error", Reason = ex.Message });
                }
            }

            // write an audit entry for the bulk apply
            await WriteAuditAsync("ApplyUsers", 0, $"Correlation:{correlationId} Count:{request.UserIds.Count}", ct).ConfigureAwait(false);

            return new ApplyUsersResult(true, outcomes, null);
        }

        // Helper to centralize audit writes for both IAuditRepository and IAuditLogger
        private async Task WriteAuditAsync(string action, long entityId, string? data, CancellationToken ct)
        {
            if (_auditRepository != null)
            {
                await _auditRepository.WriteAsync(new ProjectAudit
                {
                    Entity = "User",
                    EntityId = entityId,
                    Action = action,
                    CreatedAt = DateTimeOffset.UtcNow,
                    Data = data
                }, ct).ConfigureAwait(false);
            }
            else if (_auditLogger != null)
            {
                await _auditLogger.LogAsync($"User{action}", $"User:{entityId} {data}", ct).ConfigureAwait(false);
            }
        }
    }
}
