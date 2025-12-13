// src/Controllers/UserProfile.cs
using System;
using System.Text.Json;
using t5f25sdprojectone_projectsplus.Models.Users;

namespace t5f25sdprojectone_projectsplus.DTOs.UserDTOs
{
    /// <summary>
    /// Lightweight DTO projection of UserEntity for API responses.
    /// Keeps a stable, explicit surface area and avoids sending credential material.
    /// </summary>
    public sealed record UserProfile
    {
      
        public long Id { get; init; }
        public string Email { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? FullName { get; init; }
        public bool IsActive { get; init; }
        public bool NeedsPassword { get; init; }
        public string[] Roles { get; init; } = Array.Empty<string>();
        public JsonDocument? Profile { get; init; }
        public JsonDocument? Attributes { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset UpdatedAt { get; init; }

        public static UserProfile FromEntity(UserEntity e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            JsonDocument? TryParse(string? json)
            {
                if (string.IsNullOrWhiteSpace(json)) return null;
                try { return JsonDocument.Parse(json); }
                catch { return null; }
            }

            return new UserProfile
            {
                Id = e.Id,
                Email = e.Email,
                DisplayName = e.DisplayName,
                FirstName = e.FirstName,
                LastName = e.LastName,
                FullName = e.FullName,
                IsActive = e.IsActive,
                NeedsPassword = e.NeedsPassword,
                Roles = e.Roles?.ToArray() ?? Array.Empty<string>(),
                Profile = TryParse(e.ProfileJson),
                Attributes = TryParse(e.AttributesJson),
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            };
        }
    }
}
