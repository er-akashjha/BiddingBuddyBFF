using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class TenderAlertRuleConfiguration : IEntityTypeConfiguration<TenderAlertRule>
{
    public void Configure(EntityTypeBuilder<TenderAlertRule> b)
    {
        b.ToTable("tender_alert_rules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Name).HasColumnName("name").IsRequired();
        b.Property(x => x.Categories).HasColumnName("categories").HasColumnType("text[]");
        b.Property(x => x.States).HasColumnName("states").HasColumnType("text[]");
        b.Property(x => x.Keywords).HasColumnName("keywords").HasColumnType("text[]");
        b.Property(x => x.MinValue).HasColumnName("min_value").HasPrecision(15, 2);
        b.Property(x => x.MaxValue).HasColumnName("max_value").HasPrecision(15, 2);
        b.Property(x => x.MinAiScore).HasColumnName("min_ai_score");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.OrgId);

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
