using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgSsoDomainConfiguration : IEntityTypeConfiguration<OrgSsoDomain>
{
    public void Configure(EntityTypeBuilder<OrgSsoDomain> b)
    {
        b.ToTable("org_sso_domains");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Domain).HasColumnName("domain").IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasDefaultValue("entra");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasOne(x => x.Org)
            .WithMany()
            .HasForeignKey(x => x.OrgId)
            .OnDelete(DeleteBehavior.Cascade);

        // Migration 0032 indexes lower(domain); the service lower-cases before every write and read,
        // so a plain unique index here describes the same guarantee to EF without EF trying to
        // render the expression form.
        b.HasIndex(x => x.Domain).IsUnique();
        b.HasIndex(x => x.OrgId);
    }
}
