// src/Data/Configurations/AuditEntryEntityConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using t5f25sdprojectone_projectsplus.Models.Audit;
using t5f25sdprojectone_projectsplus.Repositories.EF;

namespace t5f25sdprojectone_projectsplus.Models.Configurations.Audit
{
    public class AuditEntryEntityConfiguration : IEntityTypeConfiguration<AuditEntryEntity>
    {
        public void Configure(EntityTypeBuilder<AuditEntryEntity> b)
        {
            b.ToTable("AuditEntries");
            b.HasKey(e => e.Id);

            b.Property(e => e.TimestampUtc)
             .IsRequired();

            b.Property(e => e.ActorUserId)
             .IsRequired(false);

            b.Property(e => e.Action)
             .IsRequired()
             .HasMaxLength(200);

            b.Property(e => e.Outcome)
             .IsRequired()
             .HasMaxLength(50);

            b.Property(e => e.Detail)
             .HasMaxLength(2000);

            b.HasIndex(e => e.ActorUserId);
            b.HasIndex(e => e.Action);
        }
    }
}

