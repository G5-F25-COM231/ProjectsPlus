//using System;
//using System.Collections.Generic;
//using System.Threading;
//using System.Threading.Tasks;
//using t5f25sdprojectone_projectsplus.Models;
//using t5f25sdprojectone_projectsplus.Models.Users;
//using t5f25sdprojectone_projectsplus.Repositories.Interfaces;
//using t5f25sdprojectone_projectsplus.Services.Interfaces;

//namespace t5f25sdprojectone_projectsplus.Services
//{
//    public class UserService : IUserService
//    {
//        private readonly IUserRepository _users;
//        private readonly IAuditLogger _auditLogger;

//        public UserService(IUserRepository users, IAuditLogger auditLogger)
//        {
//            _users = users ?? throw new ArgumentNullException(nameof(users));
//            _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
//        }

//        public async Task<UserEntity> CreateAsync(UserEntity user, CancellationToken ct = default)
//        {
//            if (user == null) throw new ArgumentNullException(nameof(user));
//            if (string.IsNullOrWhiteSpace(user.Email)) throw new ArgumentException("Email is required", nameof(user));
//            if (string.IsNullOrWhiteSpace(user.FullName)) throw new ArgumentException("FullName is required", nameof(user));

//            user.Email = user.Email.Trim().ToLowerInvariant();
//            // Note: password hashing should occur before calling repository or within repository depending on conventions
//            var created = await _users.InsertAsync(user, ct);
//            await _auditLogger.LogAsync("UserCreated", created.ToString(), ct);
//            return created;
//        }

//        public Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default)
//            => _users.FindByIdAsync(id, ct);

//        public Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<UserEntity>(null);
//            return _users.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
//        }

//        public async Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
//        {
//            if (page < 1) page = 1;
//            if (pageSize < 1) pageSize = 50;
//            return await _users.ListAsync(page, pageSize, ct);
//        }

//        public async Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
//        {
//            if (user == null) throw new ArgumentNullException(nameof(user));
//            user.Email = user.Email?.Trim().ToLowerInvariant();
//            var updated = await _users.UpdateAsync(user, ct);
//            await _auditLogger.LogAsync("UserUpdated", updated.ToString(), ct);
//            return updated;
//        }

//        public async Task DeleteAsync(long id, int expectedVersion, CancellationToken ct = default)
//        {
//            await _users.DeleteAsync(id, expectedVersion, ct);
//            await _auditLogger.LogAsync("UserDeleted", $"User:{id} v{expectedVersion}", ct);
//        }

//        public async Task<UserEntity> AuthenticateAsync(string email, string password, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) return null;
//            var user = await _users.FindByEmailAsync(email.Trim().ToLowerInvariant(), ct);
//            if (user == null) return null;

//            // Password verification is intentionally abstracted; repository may store hashed password
//            var verified = await _users.VerifyPasswordAsync(user.Id, password, ct);
//            return verified ? user : null;
//        }

//        public async Task<UserEntity> AssignRoleAsync(long userId, string role, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
//            var updated = await _users.AddRoleAsync(userId, role.Trim(), ct);
//            await _auditLogger.LogAsync("UserRoleAssigned", $"User:{userId} Role:{role}", ct);
//            return updated;
//        }

//        public async Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default)
//        {
//            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
//            var updated = await _users.RemoveRoleAsync(userId, role.Trim(), ct);
//            await _auditLogger.LogAsync("UserRoleRemoved", $"User:{userId} Role:{role}", ct);
//            return updated;
//        }
//    }
//}
