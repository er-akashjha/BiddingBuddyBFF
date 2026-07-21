using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="GrantOpportunity"/> to <c>grant_opportunities</c> (migration 0028).
///
/// <para>Every column is named explicitly — this project has no snake_case naming convention
/// plugin, so an unmapped property silently becomes a PascalCase column that does not exist and
/// fails at read time with 42703.</para>
/// </summary>
public class GrantOpportunityConfiguration : IEntityTypeConfiguration<GrantOpportunity>
{
    public void Configure(EntityTypeBuilder<GrantOpportunity> b)
    {
        b.ToTable("grant_opportunities");
        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        b.Property(x => x.Platform).HasColumnName("platform").HasDefaultValue("grants-gov").IsRequired();
        b.Property(x => x.PlatformGrantId).HasColumnName("platform_grant_id").IsRequired();
        b.Property(x => x.MongoGrantId).HasColumnName("mongo_grant_id");
        b.Property(x => x.OpportunityNumber).HasColumnName("opportunity_number");
        b.Property(x => x.SourceUrl).HasColumnName("source_url");

        b.Property(x => x.Title).HasColumnName("title").IsRequired();
        b.Property(x => x.Summary).HasColumnName("summary");
        b.Property(x => x.AgencyName).HasColumnName("agency_name");
        b.Property(x => x.AgencyCode).HasColumnName("agency_code");
        b.Property(x => x.Category).HasColumnName("category");

        b.Property(x => x.Currency).HasColumnName("currency").HasDefaultValue("USD").IsRequired();
        b.Property(x => x.AwardCeiling).HasColumnName("award_ceiling").HasPrecision(15, 2);
        b.Property(x => x.AwardFloor).HasColumnName("award_floor").HasPrecision(15, 2);
        b.Property(x => x.EstimatedTotalProgramFunding)
            .HasColumnName("estimated_total_program_funding").HasPrecision(15, 2);
        b.Property(x => x.ExpectedNumberOfAwards).HasColumnName("expected_number_of_awards");
        b.Property(x => x.CostSharingRequired).HasColumnName("cost_sharing_required");

        b.Property(x => x.PostedDate).HasColumnName("posted_date");
        b.Property(x => x.CloseDate).HasColumnName("close_date");
        b.Property(x => x.LoiDueDate).HasColumnName("loi_due_date");
        b.Property(x => x.ArchiveDate).HasColumnName("archive_date");
        b.Property(x => x.CloseDateExplanation).HasColumnName("close_date_explanation");
        b.Property(x => x.IsRolling).HasColumnName("is_rolling").HasDefaultValue(false);

        b.Property(x => x.ApplicantTypesRaw).HasColumnName("applicant_types_raw").HasColumnType("text[]");
        b.Property(x => x.ApplicantTypeCodes).HasColumnName("applicant_type_codes").HasColumnType("text[]");

        b.Property(x => x.TribalGovernmentsEligible).HasColumnName("tribal_governments_eligible");
        b.Property(x => x.TribalOrganizationsEligible).HasColumnName("tribal_organizations_eligible");
        b.Property(x => x.Nonprofit501C3Eligible).HasColumnName("nonprofit_501c3_eligible");
        b.Property(x => x.IsTribalSetAside).HasColumnName("is_tribal_set_aside");
        b.Property(x => x.NativeLedPriority).HasColumnName("native_led_priority");

        b.Property(x => x.AssistanceListingNumbers)
            .HasColumnName("assistance_listing_numbers").HasColumnType("text[]");
        b.Property(x => x.FundingInstruments).HasColumnName("funding_instruments").HasColumnType("text[]");

        b.Property(x => x.IsForecast).HasColumnName("is_forecast").HasDefaultValue(false);
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("posted").IsRequired();

        b.Property(x => x.AiScore).HasColumnName("ai_score").HasDefaultValue(0);
        b.Property(x => x.AiSummary).HasColumnName("ai_summary");
        b.Property(x => x.AiTags).HasColumnName("ai_tags").HasColumnType("text[]");

        b.Property(x => x.AlertsScannedAt).HasColumnName("alerts_scanned_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Composite, matching the migration: a grant id is only unique within its portal.
        // Deliberately NOT a single-column unique on PlatformGrantId — that is the drift
        // TenderConfiguration still carries after migration 0022 dropped its equivalent.
        b.HasIndex(x => new { x.Platform, x.PlatformGrantId }).IsUnique();

        b.HasIndex(x => x.MongoGrantId).IsUnique().HasFilter("mongo_grant_id IS NOT NULL");
        b.HasIndex(x => x.CloseDate);
        b.HasIndex(x => x.Category);
    }
}
