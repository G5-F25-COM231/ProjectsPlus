// src/Repositories/EF/RefreshTokenEntity.cs
using System;

namespace t5f25sdprojectone_projectsplus.Models.Authorization
{
    public class RefreshTokenEntity
    {
        public long Id { get; set; }
        public string Token { get; set; } = string.Empty;
        public long UserId { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public bool IsRevoked { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? RevokedAtUtc { get; set; }
    }
}
