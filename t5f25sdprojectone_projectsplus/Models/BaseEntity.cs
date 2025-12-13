using System;

namespace t5f25sdprojectone_projectsplus.Models
{
    /// <summary>
    /// Canonical base entity for ProjectsPlus.
    /// All persistent entities must inherit this to ensure stable audit fields and optimistic concurrency.
    /// </summary>
    public abstract class BaseEntity
    {
        /// <summary>
        /// Numeric primary key. Use long for compatibility with large datasets.
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// Optimistic concurrency token. Increment on each successful update.
        /// </summary>
        public int Version { get; set; } = 1;

        /// <summary>
        /// Immutable creation timestamp (UTC).
        /// </summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Last update timestamp (UTC). Must be set on every mutation.
        /// </summary>
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Deterministic ToString contract used by Ensure modules and infralog line formation.
        /// Format: Type: {Id}:key:{humanKey}:v{Version}:u{UpdatedAt:O}
        /// Derived classes should override GetHumanKey to provide a concise, stable humanKey.
        /// </summary>
        public override string ToString()
        {
            var typeName = GetType().Name;
            var humanKey = GetHumanKey()?.Replace("|", "-").Replace("\n", " ").Trim() ?? "n/a";
            // Truncate humanKey to ~40 chars as guidance
            if (humanKey.Length > 40) humanKey = humanKey.Substring(0, 40);
            return $"Type: {typeName}:key:{humanKey}:v{Version}:u{UpdatedAt:O}";
        }

        /// <summary>
        /// Derived entities must provide a stable human-readable key used by the ToString contract.
        /// Examples: User => email, Workspace => slug, Project => title (truncated).
        /// Keep this deterministic and free of sensitive data (no raw secrets).
        /// </summary>
        protected abstract string GetHumanKey();
    }
}
