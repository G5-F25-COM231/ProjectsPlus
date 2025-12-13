// DTOs/Identity/RegisterAdminSeedRequest.cs
using System.ComponentModel.DataAnnotations;

namespace t5f25sdprojectone_projectsplus.OLD.DTO.Identity
{
    /// <summary>
    /// Request payload used for seeding initial admin accounts or for admins creating other admins.
    /// - DefaultPassword is optional; if omitted the system should generate a secure one-time password and email it (do not log).
    /// - Notes can carry operator context for audit but should not contain secrets.
    /// - This DTO is intended for protected flows only (deploy-runbook or admin-only endpoints).
    /// </summary>
    public sealed class RegisterAdminSeedRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Username { get; set; } = null!;

        [Required]
        [EmailAddress]
        [StringLength(320)]
        public string Email { get; set; } = null!;

        // Optional: system-generated/default password may be sent via email; never return in API responses or logs.
        [StringLength(200)]
        public string? DefaultPassword { get; set; }

        // Optional free-form notes for operators; stored in audit alongside the create operation.
        [StringLength(2000)]
        public string? Notes { get; set; }

        // Optional actor id performing the seed; services may override with authenticated actor.
        public long? CreatedBy { get; set; }
    }
}
