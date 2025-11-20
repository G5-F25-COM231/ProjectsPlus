using System;
using System.Text.Json;
using t5f25sdprojectone_projectsplus.Models;

namespace t5f25sdprojectone_projectsplus.Models.Projects
{
    public class ProjectEntity : BaseEntity
    {
        public string Title { get; set; } = null!;
        public string? ShortDescription { get; set; }
        public string? LongDescription { get; set; }

        // OwnerUserId links to User.Id
        public long OwnerUserId { get; set; }

        // Optional workspace link if created
        public long? WorkspaceId { get; set; }

        // Project lifecycle status
        public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

        // Add-on fields
        public long? AdditionOfId { get; set; }
        public string? AdditionType { get; set; }
        public string? AdditionCompatibilityJson { get; set; }
        public bool RequiresBaseConsent { get; set; } = false;

        // Soft-delete flag
        public bool IsDeleted { get; set; } = false;
        public string? Slug { get; internal set; }

        protected override string GetHumanKey()
        {
            return (Title ?? $"project-{Id}").Trim();
        }

        public void SetAdditionCompatibilityFromObject(object? obj)
        {
            if (obj == null)
            {
                AdditionCompatibilityJson = null;
                return;
            }

            AdditionCompatibilityJson = JsonSerializer.Serialize(obj);
        }

        /// <summary>
        /// Provide a concise, machine-friendly ToString useful in logs and audit records.
        /// </summary>
        public override string ToString()
        {
            return $"Project:{Id}:Ws:{WorkspaceId?.ToString() ?? "none"}:Owner:{OwnerUserId}:Status:{Status}:v{Version}";
        }
    }
}
