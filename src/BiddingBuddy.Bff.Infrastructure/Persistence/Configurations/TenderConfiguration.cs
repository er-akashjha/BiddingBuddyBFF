using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> b)
    {
        b.ToTable("tenders");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.GemTenderId).HasColumnName("gem_tender_id").IsRequired();
        b.Property(x => x.MongoTenderId).HasColumnName("mongo_tender_id");
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.BuyerOrgName).HasColumnName("buyer_org_name");
        b.Property(x => x.BuyerOrgIdGem).HasColumnName("buyer_org_id_gem");
        b.Property(x => x.State).HasColumnName("state");
        b.Property(x => x.City).HasColumnName("city");
        b.Property(x => x.Category).HasColumnName("category");
        b.Property(x => x.SubCategory).HasColumnName("sub_category");
        b.Property(x => x.TenderValue).HasColumnName("tender_value").HasPrecision(15, 2);
        b.Property(x => x.EmdAmount).HasColumnName("emd_amount").HasPrecision(15, 2);
        b.Property(x => x.PublishedDate).HasColumnName("published_date");
        b.Property(x => x.ClosingDate).HasColumnName("closing_date");
        b.Property(x => x.DeliveryDays).HasColumnName("delivery_days");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("active");
        b.Property(x => x.CorrigendumCount).HasColumnName("corrigendum_count").HasDefaultValue(0);
        b.Property(x => x.AiScore).HasColumnName("ai_score");
        b.Property(x => x.EligibilityScore).HasColumnName("eligibility_score");
        b.Property(x => x.WinProbability).HasColumnName("win_probability").HasPrecision(5, 2);
        b.Property(x => x.RiskScore).HasColumnName("risk_score");
        b.Property(x => x.AiSummary).HasColumnName("ai_summary");
        b.Property(x => x.AiTags).HasColumnName("ai_tags").HasColumnType("text[]");
        b.Property(x => x.RawData).HasColumnName("raw_data").HasColumnType("jsonb");
        b.Property(x => x.Source).HasColumnName("source").HasDefaultValue("gem_pipeline");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.AlertsScannedAt).HasColumnName("alerts_scanned_at");

        b.HasIndex(x => x.GemTenderId).IsUnique();
        b.HasIndex(x => x.MongoTenderId).IsUnique().HasFilter("mongo_tender_id IS NOT NULL");
        b.HasIndex(x => x.ClosingDate);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.AiScore);

        b.HasMany(x => x.Documents).WithOne(x => x.Tender).HasForeignKey(x => x.TenderId);
        b.HasMany(x => x.OrgSettings).WithOne(x => x.Tender).HasForeignKey(x => x.TenderId);
        b.HasOne(x => x.AiAnalysis).WithOne(x => x.Tender).HasForeignKey<AiAnalysisResult>(x => x.TenderId);
    }
}
