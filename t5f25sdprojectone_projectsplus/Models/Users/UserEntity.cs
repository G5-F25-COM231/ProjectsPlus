using System;
using System.Text.Json;

namespace t5f25sdprojectone_projectsplus.Models.Users
{
    /// <summary>
    /// Persistent representation of a user.
    /// AttributesJson stores ABAC attributes or policy fragments as minified JSON.
    /// DO NOT store raw secrets here.
    /// </summary>
    public class UserEntity : BaseEntity
    {
        public string Email { get; set; }
        public string DisplayName { get; set; }
        public string ProviderId { get; set; }         // optional external provider id
        public string AttributesJson { get; set; }    // JSON blob, minified
        public bool IsDeleted { get; set; } = false;
        public bool IsActive { get; set; } = true;

        protected override string GetHumanKey()
        {
            return Email?.ToLowerInvariant() ?? $"user-{Id}";
        }

        public void SetAttributesJsonFromObject(object obj)
        {
            if (obj == null)
            {
                AttributesJson = null;
                return;
            }

            // store deterministic/minified JSON
            AttributesJson = JsonSerializer.Serialize(obj);
        }
    }
}
