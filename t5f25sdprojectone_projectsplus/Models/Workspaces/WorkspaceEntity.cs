// src/ProjectsPlus.Models/Workspaces/WorkspaceEntity.cs
using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.Workspaces
{
    public class WorkspaceEntity : BaseEntity
    {
        public long ProjectId { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }                   // unique per workspace (scoped)
        public WorkspaceState State { get; set; } = WorkspaceState.ProvisioningPending;
        public string MetadataJson { get; set; }           // provider references and metadata
        public long OwnerUserId { get; set; }
        public bool IsDeleted { get; internal set; }

        // --- Communications / notifications related fields ---

        // Workspace-level settings (JSON). Use configuration class mapping in separate configuration file.
        public string? SettingsJson { get; set; }

        // Default notification preferences for workspace announcements (JSON)
        public string? DefaultNotificationPreferencesJson { get; set; }

        // Retention policy metadata (JSON) - e.g., { "messagesDays": 365, "attachmentsDays": 3650 }
        public string? RetentionPolicyJson { get; set; }

        // Timestamps for auditing and ordering
        //public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        //public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        protected override string GetHumanKey()
        {
            return Slug ?? $"ws-{Id}";
        }

        public void SetMetadataFromObject(object obj)
        {
            if (obj == null) { MetadataJson = null; UpdatedAt = DateTimeOffset.UtcNow; return; }
            MetadataJson = JsonSerializer.Serialize(obj);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public T? GetMetadataAsObject<T>()
        {
            if (string.IsNullOrWhiteSpace(MetadataJson)) return default;
            try { return JsonSerializer.Deserialize<T>(MetadataJson); }
            catch { return default; }
        }

        public void SetSettingsFromObject(object obj)
        {
            if (obj == null) { SettingsJson = null; UpdatedAt = DateTimeOffset.UtcNow; return; }
            SettingsJson = JsonSerializer.Serialize(obj);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public T? GetSettingsAsObject<T>()
        {
            if (string.IsNullOrWhiteSpace(SettingsJson)) return default;
            try { return JsonSerializer.Deserialize<T>(SettingsJson); }
            catch { return default; }
        }

        public void SetDefaultNotificationPreferences(object prefs)
        {
            if (prefs == null) { DefaultNotificationPreferencesJson = null; UpdatedAt = DateTimeOffset.UtcNow; return; }
            DefaultNotificationPreferencesJson = JsonSerializer.Serialize(prefs);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public T? GetDefaultNotificationPreferences<T>()
        {
            if (string.IsNullOrWhiteSpace(DefaultNotificationPreferencesJson)) return default;
            try { return JsonSerializer.Deserialize<T>(DefaultNotificationPreferencesJson); }
            catch { return default; }
        }

        public void SetRetentionPolicy(object policy)
        {
            if (policy == null) { RetentionPolicyJson = null; UpdatedAt = DateTimeOffset.UtcNow; return; }
            RetentionPolicyJson = JsonSerializer.Serialize(policy);
            UpdatedAt = DateTimeOffset.UtcNow;
        }

        public T? GetRetentionPolicyAsObject<T>()
        {
            if (string.IsNullOrWhiteSpace(RetentionPolicyJson)) return default;
            try { return JsonSerializer.Deserialize<T>(RetentionPolicyJson); }
            catch { return default; }
        }
    }
}
