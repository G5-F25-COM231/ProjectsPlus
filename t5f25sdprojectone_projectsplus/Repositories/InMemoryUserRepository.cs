using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using t5f25sdprojectone_projectsplus.Models;
using t5f25sdprojectone_projectsplus.Models.Users;
using t5f25sdprojectone_projectsplus.Repositories;
using t5f25sdprojectone_projectsplus.Repositories.Interfaces;

namespace t5f25sdprojectone_projectsplus.Repositories.InMemory
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
                user.CreatedAt = DateTimeOffset.UtcNow;
                user.UpdatedAt = user.CreatedAt;
                user.Version = 1;
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
                if (_emailIndex.TryGetValue(email.Trim(), out var id) && _byId.TryGetValue(id, out var u) && !u.IsDeleted)
                    return Task.FromResult(Clone(u));
                return Task.FromResult<UserEntity>(null);
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

        // simple deep clone to avoid test side-effects
        private static UserEntity Clone(UserEntity u)
        {
            if (u == null) return null;
            return new UserEntity
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                ProviderId = u.ProviderId,
                AttributesJson = u.AttributesJson,
                IsDeleted = u.IsDeleted,
                IsActive = u.IsActive,
                Version = u.Version,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            };
        }
    }
}
