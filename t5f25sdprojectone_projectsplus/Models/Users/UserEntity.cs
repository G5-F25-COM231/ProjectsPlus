using System;
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

        public List<string> Roles { get; internal set; }

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
    }
}
