// src/ProjectsPlus.Models/Users/UserEntity.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.Users
{
    [Table("users")]
    public sealed class UserEntity : BaseEntity
    {
        [Key]
        [Column("id")]
        public long Id { get; set; }

        [Column("system_type_id")]
        public long? SystemTypeId { get; set; }

        [Required]
        [MaxLength(320)]
        [Column("email")]
        public string Email { get; set; } = null!;

        [Required]
        [MaxLength(320)]
        [Column("normalized_email")]
        public string NormalizedEmail { get; set; } = null!;

        [MaxLength(200)]
        [Column("display_name")]
        public string? DisplayName { get; set; }

        [MaxLength(250)]
        [Column("first_name")]
        public string? FirstName { get; set; }

        [MaxLength(250)]
        [Column("last_name")]
        public string? LastName { get; set; }

        [MaxLength(2000)]
        [Column("password_hash")]
        public string? PasswordHash { get; set; }

        [MaxLength(500)]
        [Column("password_salt")]
        public string? PasswordSalt { get; set; }

        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("is_deleted")]
        public bool IsDeleted { get; set; } = false;

        [MaxLength(200)]
        [Column("provider_id")]
        public string? ProviderId { get; set; }

        [MaxLength(4000)]
        [Column("attributes_json")]
        public string? AttributesJson { get; set; }

        [MaxLength(4000)]
        [Column("profile_json")]
        public string? ProfileJson { get; set; }

        [Column("created_by")]
        public long? CreatedBy { get; set; }

        [Required]
        [Column("created_at")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [Column("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [ConcurrencyCheck]
        [Column("version")]
        public int Version { get; set; } = 1;

        [NotMapped]
        public string? FullName
        {
            get
            {
                var hasFirst = !string.IsNullOrWhiteSpace(FirstName);
                var hasLast = !string.IsNullOrWhiteSpace(LastName);

                if (hasFirst && hasLast) return $"{FirstName!.Trim()} {LastName!.Trim()}";
                if (hasFirst) return FirstName!.Trim();
                if (hasLast) return LastName!.Trim();
                return string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName;
            }
        }

        [NotMapped]
        public bool NeedsPassword => string.IsNullOrWhiteSpace(PasswordHash) && string.IsNullOrWhiteSpace(ProviderId);

        public List<string> Roles { get; internal set; } = new();

        public override string ToString()
        {
            return $"User:{Id}:E={NormalizedEmail}:Active={IsActive}:Del={IsDeleted}:Ver={Version}";
        }

        public void SetProfile(object profile)
        {
            ProfileJson = profile == null ? null : JsonSerializer.Serialize(profile);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void SetAttributes(object attributes)
        {
            AttributesJson = attributes == null ? null : JsonSerializer.Serialize(attributes);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        protected override string GetHumanKey()
        {
            if (!string.IsNullOrWhiteSpace(NormalizedEmail)) return NormalizedEmail;
            if (!string.IsNullOrWhiteSpace(FullName)) return FullName!;
            return $"user-{Id}";
        }

        // --- New fields for communications / realtime ---

        // Last seen timestamp (UTC)
        [Column("last_seen_utc")]
        public DateTimeOffset? LastSeenUtc { get; set; }

        // Presence status (string stored for flexibility; map to enum in app layer)
        [MaxLength(64)]
        [Column("presence_status")]
        public string? PresenceStatus { get; set; }

        // Optional persisted connection ids JSON (prefer Redis for multi-node; persisted for single-node or admin queries)
        [MaxLength(4000)]
        [Column("active_connection_ids_json")]
        public string? ActiveConnectionIdsJson { get; set; }

        // Notification preferences (JSON) - per-user defaults for channels and preferences
        [MaxLength(4000)]
        [Column("notification_preferences_json")]
        public string? NotificationPreferencesJson { get; set; }

        // Device tokens for push notifications (JSON array of tokens and metadata)
        [MaxLength(4000)]
        [Column("device_tokens_json")]
        public string? DeviceTokensJson { get; set; }

        // --- Helper methods for presence, connections, and device tokens ---

        public void MarkSeen(DateTimeOffset seenAt)
        {
            LastSeenUtc = seenAt;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void SetPresence(string presence)
        {
            PresenceStatus = presence;
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public IReadOnlyList<string> GetActiveConnectionIds()
        {
            if (string.IsNullOrWhiteSpace(ActiveConnectionIdsJson)) return Array.Empty<string>();
            try { return JsonSerializer.Deserialize<IReadOnlyList<string>>(ActiveConnectionIdsJson) ?? Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        public void AddConnectionId(string connectionId)
        {
            var list = new List<string>(GetActiveConnectionIds());
            if (!list.Contains(connectionId)) list.Add(connectionId);
            ActiveConnectionIdsJson = JsonSerializer.Serialize(list);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void RemoveConnectionId(string connectionId)
        {
            var list = new List<string>(GetActiveConnectionIds());
            if (list.Remove(connectionId))
            {
                ActiveConnectionIdsJson = list.Count == 0 ? null : JsonSerializer.Serialize(list);
                UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        public T? GetNotificationPreferences<T>()
        {
            if (string.IsNullOrWhiteSpace(NotificationPreferencesJson)) return default;
            try { return JsonSerializer.Deserialize<T>(NotificationPreferencesJson); }
            catch { return default; }
        }

        public void SetNotificationPreferences(object prefs)
        {
            NotificationPreferencesJson = prefs == null ? null : JsonSerializer.Serialize(prefs);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public IReadOnlyList<(string Token, string Platform)> GetDeviceTokens()
        {
            if (string.IsNullOrWhiteSpace(DeviceTokensJson)) return Array.Empty<(string, string)>();
            try
            {
                var arr = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(DeviceTokensJson);
                if (arr == null) return Array.Empty<(string, string)>();
                var outList = new List<(string, string)>();
                foreach (var d in arr)
                {
                    d.TryGetValue("token", out var token);
                    d.TryGetValue("platform", out var platform);
                    if (!string.IsNullOrWhiteSpace(token)) outList.Add((token!, platform ?? string.Empty));
                }
                return outList;
            }
            catch { return Array.Empty<(string, string)>(); }
        }

        public void AddOrUpdateDeviceToken(string token, string platform)
        {
            var list = new List<Dictionary<string, string>>();
            if (!string.IsNullOrWhiteSpace(DeviceTokensJson))
            {
                try { list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(DeviceTokensJson) ?? new List<Dictionary<string, string>>(); }
                catch { list = new List<Dictionary<string, string>>(); }
            }

            // upsert
            var existing = list.Find(d => d.TryGetValue("token", out var t) && t == token);
            if (existing != null)
            {
                existing["platform"] = platform;
            }
            else
            {
                list.Add(new Dictionary<string, string> { ["token"] = token, ["platform"] = platform });
            }

            DeviceTokensJson = JsonSerializer.Serialize(list);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public void RemoveDeviceToken(string token)
        {
            if (string.IsNullOrWhiteSpace(DeviceTokensJson)) return;
            try
            {
                var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(DeviceTokensJson) ?? new List<Dictionary<string, string>>();
                var removed = list.RemoveAll(d => d.TryGetValue("token", out var t) && t == token);
                DeviceTokensJson = list.Count == 0 ? null : JsonSerializer.Serialize(list);
                if (removed > 0) UpdatedAt = DateTimeOffset.UtcNow;
            }
            catch { }
        }
    }
}
