// src/Data/Configurations/RefreshTokenEntityConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Authorization;
using t5f25sdprojectone_projectsplus.Repositories.EF;

namespace t5f25sdprojectone_projectsplus.Data.Configurations.Audit
{
    public class RefreshTokenEntityConfiguration : IEntityTypeConfiguration<RefreshTokenEntity>
    {
        public void Configure(EntityTypeBuilder<RefreshTokenEntity> b)
        {
            b.ToTable("RefreshTokens");
            b.HasKey(rt => rt.Id);

            b.Property(rt => rt.Token)
             .IsRequired()
             .HasMaxLength(500);

            b.Property(rt => rt.UserId)
             .IsRequired();

            b.Property(rt => rt.ExpiresAtUtc)
             .IsRequired();

            b.Property(rt => rt.IsRevoked)
             .IsRequired();

            b.Property(rt => rt.CreatedAtUtc)
             .IsRequired();

            b.Property(rt => rt.RevokedAtUtc)
             .IsRequired(false);

            b.HasIndex(rt => rt.Token)
             .IsUnique();

            b.HasIndex(rt => rt.UserId);
        }
    }
}
