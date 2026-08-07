using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

// Every column is explicitly .HasColumnName-mapped — this project has no snake_case convention
// plugin, so an unmapped property becomes a PascalCase column that doesn't exist (Postgres 42703).

public class OrgCapabilityProfileConfiguration : IEntityTypeConfiguration<OrgCapabilityProfile>
{
    public void Configure(EntityTypeBuilder<OrgCapabilityProfile> b)
    {
        b.ToTable("org_capability_profile");
        // The org IS the key — one profile per org, so there is no surrogate id to drift.
        b.HasKey(x => x.OrgId);
        b.Property(x => x.OrgId).HasColumnName("org_id");

        b.Property(x => x.TurnoverFy1).HasColumnName("turnover_fy1").HasPrecision(18, 2);
        b.Property(x => x.TurnoverFy2).HasColumnName("turnover_fy2").HasPrecision(18, 2);
        b.Property(x => x.TurnoverFy3).HasColumnName("turnover_fy3").HasPrecision(18, 2);
        b.Property(x => x.TurnoverFy1Label).HasColumnName("turnover_fy1_label");
        b.Property(x => x.NetWorth).HasColumnName("net_worth").HasPrecision(18, 2);
        b.Property(x => x.IncorporationDate).HasColumnName("incorporation_date");

        b.Property(x => x.UdyamNumber).HasColumnName("udyam_number");
        b.Property(x => x.UdyamCategory).HasColumnName("udyam_category");
        b.Property(x => x.DpiitStartupNumber).HasColumnName("dpiit_startup_number");
        b.Property(x => x.NsicNumber).HasColumnName("nsic_number");

        b.Property(x => x.ServiceableStates).HasColumnName("serviceable_states");
        b.Property(x => x.CategoriesSupplied).HasColumnName("categories_supplied");

        b.Property(x => x.EmdHeadroom).HasColumnName("emd_headroom").HasPrecision(18, 2);
        b.Property(x => x.BgLimit).HasColumnName("bg_limit").HasPrecision(18, 2);
        b.Property(x => x.BgUtilised).HasColumnName("bg_utilised").HasPrecision(18, 2);

        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class OrgCredentialConfiguration : IEntityTypeConfiguration<OrgCredential>
{
    public void Configure(EntityTypeBuilder<OrgCredential> b)
    {
        b.ToTable("org_credentials");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        b.Property(x => x.Code).HasColumnName("code").IsRequired();
        b.Property(x => x.Label).HasColumnName("label");
        b.Property(x => x.Number).HasColumnName("number");
        b.Property(x => x.IssuedAt).HasColumnName("issued_at");
        b.Property(x => x.ValidUntil).HasColumnName("valid_until");
        b.Property(x => x.DocumentId).HasColumnName("document_id");
        b.Property(x => x.VerifiedAt).HasColumnName("verified_at");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.Kind, x.Code }).IsUnique();
        b.HasIndex(x => x.OrgId);

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        // Link, not ownership: deleting the proof file leaves the claim standing.
        b.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class TenderFitReportConfiguration : IEntityTypeConfiguration<TenderFitReport>
{
    public void Configure(EntityTypeBuilder<TenderFitReport> b)
    {
        b.ToTable("tender_fit_reports");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.TenderId).HasColumnName("tender_id");
        b.Property(x => x.Verdict).HasColumnName("verdict").IsRequired();
        b.Property(x => x.VerdictReason).HasColumnName("verdict_reason");
        b.Property(x => x.Findings).HasColumnName("findings").HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.CostToBid).HasColumnName("cost_to_bid").HasPrecision(18, 2);
        b.Property(x => x.CostBreakdown).HasColumnName("cost_breakdown").HasColumnType("jsonb");
        b.Property(x => x.RuleSetVersion).HasColumnName("rule_set_version").IsRequired();
        b.Property(x => x.ProfileUpdatedAt).HasColumnName("profile_updated_at");
        b.Property(x => x.TenderUpdatedAt).HasColumnName("tender_updated_at");
        b.Property(x => x.CorrigendumCount).HasColumnName("corrigendum_count");
        b.Property(x => x.Narrative).HasColumnName("narrative");
        b.Property(x => x.NarrativeModel).HasColumnName("narrative_model");
        b.Property(x => x.NarrativeState).HasColumnName("narrative_state").HasDefaultValue("pending");
        b.Property(x => x.NarrativeError).HasColumnName("narrative_error");
        b.Property(x => x.GeneratedAt).HasColumnName("generated_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.TenderId }).IsUnique();

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Tender).WithMany().HasForeignKey(x => x.TenderId);
    }
}
