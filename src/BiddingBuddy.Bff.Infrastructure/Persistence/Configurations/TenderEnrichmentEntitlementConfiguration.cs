using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class TenderEnrichmentEntitlementConfiguration : IEntityTypeConfiguration<TenderEnrichmentEntitlement>
{
    public void Configure(EntityTypeBuilder<TenderEnrichmentEntitlement> b)
    {
        b.ToTable("tender_enrichment_entitlements");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id").IsRequired();
        b.Property(x => x.GemTenderId).HasColumnName("gem_tender_id").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.Source).HasColumnName("source").HasDefaultValue("grant");
        b.Property(x => x.PaymentRef).HasColumnName("payment_ref");
        b.Property(x => x.UnlockedByUserId).HasColumnName("unlocked_by_user_id");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.UnlockedAt).HasColumnName("unlocked_at");

        b.HasIndex(x => new { x.OrgId, x.GemTenderId }).IsUnique();
        b.HasIndex(x => x.OrgId);
        b.HasIndex(x => x.GemTenderId);
    }
}
