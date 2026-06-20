using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class TenderMatchConfiguration : IEntityTypeConfiguration<TenderMatch>
{
    public void Configure(EntityTypeBuilder<TenderMatch> b)
    {
        b.ToTable("tender_matches");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.RuleId).HasColumnName("rule_id");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("pending");
        b.Property(x => x.MatchedAt).HasColumnName("matched_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.BatchId).HasColumnName("batch_id");
        b.Property(x => x.SentAt).HasColumnName("sent_at");

        b.HasIndex(x => new { x.OrgId, x.TenderId }).IsUnique();
        b.HasIndex(x => new { x.OrgId, x.Status });

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId);
    }
}
