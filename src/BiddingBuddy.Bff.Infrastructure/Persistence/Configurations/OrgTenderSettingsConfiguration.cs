using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgTenderSettingsConfiguration : IEntityTypeConfiguration<OrgTenderSettings>
{
    public void Configure(EntityTypeBuilder<OrgTenderSettings> b)
    {
        b.ToTable("org_tender_settings");
        b.HasKey(x => new { x.OrgId, x.TenderId });
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.IsTracked).HasColumnName("is_tracked").HasDefaultValue(false);
        b.Property(x => x.IsSaved).HasColumnName("is_saved").HasDefaultValue(false);
        b.Property(x => x.CustomScore).HasColumnName("custom_score");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.Tags).HasColumnName("tags").HasColumnType("text[]");
        b.Property(x => x.AddedBy).HasColumnName("added_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        // Explicit FK declarations — prevents EF from generating shadow 'OrganizationId' / 'TenderId' columns
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
