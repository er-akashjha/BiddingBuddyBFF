using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

// Every column is explicitly .HasColumnName-mapped — this project has no snake_case convention
// plugin, so an unmapped property becomes a PascalCase column that doesn't exist (Postgres 42703).
public class SavedGrantConfiguration : IEntityTypeConfiguration<SavedGrant>
{
    public void Configure(EntityTypeBuilder<SavedGrant> b)
    {
        b.ToTable("saved_grants");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.MongoGrantId).HasColumnName("mongo_grant_id").IsRequired();
        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.AgencyName).HasColumnName("agency_name");
        b.Property(x => x.OpportunityNumber).HasColumnName("opportunity_number");
        b.Property(x => x.Category).HasColumnName("category");
        b.Property(x => x.CloseDate).HasColumnName("close_date");
        b.Property(x => x.AwardCeiling).HasColumnName("award_ceiling").HasPrecision(15, 2);
        b.Property(x => x.Currency).HasColumnName("currency").HasDefaultValue("USD");
        b.Property(x => x.IsForecast).HasColumnName("is_forecast").HasDefaultValue(false);
        b.Property(x => x.SourceUrl).HasColumnName("source_url");
        b.Property(x => x.SavedBy).HasColumnName("saved_by");
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.MongoGrantId }).IsUnique();
        b.HasIndex(x => x.OrgId);

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}
