using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories
{
    public class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<long, UserEntity> _byId = new();
        private readonly Dictionary<string, long> _emailIndex = new(StringComparer.OrdinalIgnoreCase);
        private long _nextId = 2000;
        private readonly object _lock = new();

        public Task<UserEntity> InsertAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            lock (_lock)
            {
                var email = user.Email?.Trim();
                if (!string.IsNullOrWhiteSpace(email) && _emailIndex.ContainsKey(email))
                    throw new DomainConflictException($"A user with email '{email}' already exists.");

                user.Id = _nextId++;
                user.Email = email;
                user.NormalizedEmail = email?.ToLowerInvariant();
                user.CreatedAt = DateTimeOffset.UtcNow;
                user.UpdatedAt = user.CreatedAt;
                user.Version = 1;
                user.Roles ??= new List<string>();
                _byId[user.Id] = Clone(user);
                if (!string.IsNullOrWhiteSpace(email)) _emailIndex[email] = user.Id;
                return Task.FromResult(Clone(user));
            }
        }

        public Task<UserEntity> FindByIdAsync(long id, CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (_byId.TryGetValue(id, out var u) && !u.IsDeleted) return Task.FromResult(Clone(u));
                return Task.FromResult<UserEntity>(null);
            }
        }

        public Task<UserEntity> FindByEmailAsync(string email, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<UserEntity>(null);
            lock (_lock)
            {
                var key = email.Trim();
                if (_emailIndex.TryGetValue(key, out var id) && _byId.TryGetValue(id, out var u) && !u.IsDeleted)
                    return Task.FromResult(Clone(u));
                return Task.FromResult<UserEntity>(null);
            }
        }

        public Task<IReadOnlyList<UserEntity>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;

            lock (_lock)
            {
                var items = _byId.Values
                    .Where(u => !u.IsDeleted)
                    .OrderBy(u => u.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(Clone)
                    .ToList()
                    .AsReadOnly();

                return Task.FromResult((IReadOnlyList<UserEntity>)items);
            }
        }

        public Task<UserEntity> UpdateAsync(UserEntity user, CancellationToken ct = default)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            lock (_lock)
            {
                if (!_byId.TryGetValue(user.Id, out var existing) || existing.IsDeleted)
                    throw new DomainNotFoundException($"User {user.Id} not found.");

                if (existing.Version != user.Version)
                    throw new DomainConcurrencyException($"Version mismatch updating user {user.Id}.");

                // enforce email uniqueness if changed
                var newEmail = user.Email?.Trim();
                if (!string.Equals(existing.Email, newEmail, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(newEmail))
                {
                    if (_emailIndex.ContainsKey(newEmail)) throw new DomainConflictException($"A user with email '{newEmail}' already exists.");
                    // remove old index
                    if (!string.IsNullOrWhiteSpace(existing.Email)) _emailIndex.Remove(existing.Email);
                    _emailIndex[newEmail] = user.Id;
                }

                var now = DateTimeOffset.UtcNow;
                existing.DisplayName = user.DisplayName;
                existing.AttributesJson = user.AttributesJson;
                existing.ProviderId = user.ProviderId;
                existing.IsActive = user.IsActive;
                existing.Email = newEmail;
                existing.NormalizedEmail = newEmail?.ToLowerInvariant();
                // preserve existing.PasswordHash unless caller set one
                if (!string.IsNullOrEmpty(user.PasswordHash))
                    existing.PasswordHash = user.PasswordHash;
                // merge roles if caller provided (replace semantics)
                if (user.Roles != null)
                    existing.Roles = new List<string>(user.Roles);
                existing.UpdatedAt = now;
                existing.Version += 1;

                _byId[user.Id] = Clone(existing);
                return Task.FromResult(Clone(existing));
            }
        }

        public Task DeleteAsync(long userId, int expectedVersion, CancellationToken ct = default)
        {
            lock (_lock)
            {
                if (!_byId.TryGetValue(userId, out var existing) || existing.IsDeleted)
                    throw new DomainNotFoundException($"User {userId} not found.");

                if (existing.Version != expectedVersion)
                    throw new DomainConcurrencyException($"Version mismatch deleting user {userId}.");

                existing.IsDeleted = true;
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                existing.Version += 1;
                if (!string.IsNullOrWhiteSpace(existing.Email)) _emailIndex.Remove(existing.Email);
                _byId[userId] = Clone(existing);
                return Task.CompletedTask;
            }
        }

        public Task<bool> VerifyPasswordAsync(long userId, string plainTextPassword, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(plainTextPassword)) return Task.FromResult(false);

            lock (_lock)
            {
                if (!_byId.TryGetValue(userId, out var user) || user.IsDeleted) return Task.FromResult(false);
                if (string.IsNullOrEmpty(user.PasswordHash)) return Task.FromResult(false);

                // NOTE: In-memory implementation treats PasswordHash as a stored credential.
                // For tests this allows either storing a plain-text password (not for production)
                // or a hashed value produced by your chosen hasher. We compare in timing-safe manner.
                var stored = user.PasswordHash;
                var matches = FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(plainTextPassword))
                           || FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(ComputeSimpleHash(plainTextPassword)));
                return Task.FromResult(matches);
            }
        }

        public Task<UserEntity> AddRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();

            lock (_lock)
            {
                if (!_byId.TryGetValue(userId, out var existing) || existing.IsDeleted)
                    throw new DomainNotFoundException($"User {userId} not found.");

                if (existing.Roles == null) existing.Roles = new List<string>();

                if (!existing.Roles.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
                {
                    existing.Roles.Add(cleaned);
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    existing.Version += 1;
                    _byId[userId] = Clone(existing);
                }

                return Task.FromResult(Clone(existing));
            }
        }

        public Task<UserEntity> RemoveRoleAsync(long userId, string role, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(role)) throw new ArgumentException(nameof(role));
            var cleaned = role.Trim();

            lock (_lock)
            {
                if (!_byId.TryGetValue(userId, out var existing) || existing.IsDeleted)
                    throw new DomainNotFoundException($"User {userId} not found.");

                if (existing.Roles != null && existing.Roles.RemoveAll(r => string.Equals(r, cleaned, StringComparison.OrdinalIgnoreCase)) > 0)
                {
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                    existing.Version += 1;
                    _byId[userId] = Clone(existing);
                }

                return Task.FromResult(Clone(existing));
            }
        }

        // simple deep clone to avoid test side-effects
        private static UserEntity Clone(UserEntity u)
        {
            if (u == null) return null;
            return new UserEntity
            {
                Id = u.Id,
                Email = u.Email,
                NormalizedEmail = u.NormalizedEmail,
                DisplayName = u.DisplayName,
                ProviderId = u.ProviderId,
                AttributesJson = u.AttributesJson,
                IsDeleted = u.IsDeleted,
                IsActive = u.IsActive,
                Version = u.Version,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                PasswordHash = u.PasswordHash,
                Roles = u.Roles != null ? new List<string>(u.Roles) : new List<string>()
            };
        }

        // timing-safe byte comparison
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;

            var diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // helper: a simple deterministic hash for test-friendly comparisons (NOT suitable for production)
        private static string ComputeSimpleHash(string input)
        {
            // quick SHA256 hex; keeps test determinism if you choose to store hashed values
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
