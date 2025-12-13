// DTOs/Identity/RegisterExternalRequest.cs
using System.ComponentModel.DataAnnotations;

namespace t5f25sdprojectone_projectsplus.OLD.DTO.Identity
{
    /// <summary>
    /// Request payload for External signup route.
    /// - JoiningPurpose and SupportingDocumentsUploadId are required for External flow.
    /// - Password is optional because external accounts may be approved and issued a one-time default password by admins.
    /// - InviteCode is optional to support early-launch allow-lists.
    /// </summary>
    public sealed class RegisterExternalRequest
    {
        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Username { get; set; } = null!;

        [Required]
        [EmailAddress]
        [StringLength(320)]
        public string Email { get; set; } = null!;

        // Optional client-supplied password; if omitted admin will issue default one-time password on approval
        [StringLength(200)]
        public string? Password { get; set; }

        [Required]
        [StringLength(4000, MinimumLength = 20)]
        public string JoiningPurpose { get; set; } = null!;

        // Reference to a previously uploaded supporting documents archive (object store metadata id)
        [Required]
        [StringLength(2000)]
        public string SupportingDocumentsUploadId { get; set; } = null!;

        // Optional invite code or domain allow-list marker
        [StringLength(100)]
        public string? InviteCode { get; set; }

        // Optional client-provided client IP or telemetry; services may override with server-observed IP
        [StringLength(45)]
        public string? ClientIp { get; set; }
    }
}
