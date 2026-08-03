using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mappings for the buyer-side tendering tables introduced by migration 0031.
/// Grouped in one file because they are one feature and are always read together.
/// </summary>
public class TenderDraftConfiguration : IEntityTypeConfiguration<TenderDraft>
{
    public void Configure(EntityTypeBuilder<TenderDraft> b)
    {
        b.ToTable("tender_drafts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.ReferenceCode).HasColumnName("reference_code");
        b.Property(x => x.DepartmentReference).HasColumnName("department_reference");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("draft");

        b.Property(x => x.Title).HasColumnName("title");
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.ScopeOfWork).HasColumnName("scope_of_work");
        b.Property(x => x.TenderType).HasColumnName("tender_type");
        b.Property(x => x.ProcurementCategory).HasColumnName("procurement_category");
        b.Property(x => x.FormOfContract).HasColumnName("form_of_contract");
        b.Property(x => x.BiddingSystem).HasColumnName("bidding_system");
        b.Property(x => x.EvaluationMethod).HasColumnName("evaluation_method");
        b.Property(x => x.TechnicalWeightage).HasColumnName("technical_weightage");
        b.Property(x => x.GfrRuleCited).HasColumnName("gfr_rule_cited");

        b.Property(x => x.Category).HasColumnName("category");
        b.Property(x => x.State).HasColumnName("state");
        b.Property(x => x.City).HasColumnName("city");
        b.Property(x => x.Pincode).HasColumnName("pincode");

        b.Property(x => x.EstimatedValue).HasColumnName("estimated_value");
        b.Property(x => x.ValueDisclosed).HasColumnName("value_disclosed").HasDefaultValue(true);
        b.Property(x => x.EmdAmount).HasColumnName("emd_amount");
        b.Property(x => x.EmdPercentage).HasColumnName("emd_percentage");
        b.Property(x => x.EmdMode).HasColumnName("emd_mode");
        b.Property(x => x.EmdExemptions).HasColumnName("emd_exemptions").HasColumnType("text[]");
        b.Property(x => x.TenderFee).HasColumnName("tender_fee");
        b.Property(x => x.TenderFeeExemptions).HasColumnName("tender_fee_exemptions").HasColumnType("text[]");
        b.Property(x => x.PerformanceSecurityPct).HasColumnName("performance_security_pct");
        b.Property(x => x.BidValidityDays).HasColumnName("bid_validity_days");

        b.Property(x => x.PublishedAt).HasColumnName("published_at");
        b.Property(x => x.DocDownloadStart).HasColumnName("doc_download_start");
        b.Property(x => x.DocDownloadEnd).HasColumnName("doc_download_end");
        b.Property(x => x.ClarificationStart).HasColumnName("clarification_start");
        b.Property(x => x.ClarificationEnd).HasColumnName("clarification_end");
        b.Property(x => x.PrebidMeetingAt).HasColumnName("prebid_meeting_at");
        b.Property(x => x.PrebidVenue).HasColumnName("prebid_venue");
        b.Property(x => x.BidSubmissionStart).HasColumnName("bid_submission_start");
        b.Property(x => x.BidSubmissionEnd).HasColumnName("bid_submission_end");
        b.Property(x => x.TechnicalOpeningAt).HasColumnName("technical_opening_at");
        b.Property(x => x.FinancialOpeningAt).HasColumnName("financial_opening_at");

        b.Property(x => x.MseReservationPct).HasColumnName("mse_reservation_pct");
        b.Property(x => x.MiiApplicable).HasColumnName("mii_applicable");
        b.Property(x => x.MiiLocalContentPct).HasColumnName("mii_local_content_pct");
        b.Property(x => x.MiiClassRestriction).HasColumnName("mii_class_restriction");
        b.Property(x => x.LbsDeclarationRequired).HasColumnName("lbs_declaration_required");
        b.Property(x => x.StartupRelaxation).HasColumnName("startup_relaxation");
        b.Property(x => x.IntegrityPactApplicable).HasColumnName("integrity_pact_applicable");
        b.Property(x => x.IntegrityPactMonitor).HasColumnName("integrity_pact_monitor");
        b.Property(x => x.GemarptsReference).HasColumnName("gemarpts_reference");

        b.Property(x => x.Detail).HasColumnName("detail").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        b.Property(x => x.RuleSetVersion).HasColumnName("rule_set_version");
        b.Property(x => x.MongoTenderId).HasColumnName("mongo_tender_id");
        b.Property(x => x.CurrentVersion).HasColumnName("current_version").HasDefaultValue(0);

        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.PublishedBy).HasColumnName("published_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => x.ReferenceCode).IsUnique();
        b.HasIndex(x => new { x.OrgId, x.Status });

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasMany(x => x.Versions).WithOne(v => v.Draft).HasForeignKey(v => v.DraftId);
        b.HasMany(x => x.Corrigenda).WithOne(c => c.Draft).HasForeignKey(c => c.DraftId);
        b.HasMany(x => x.CommitteeMembers).WithOne(m => m.Draft).HasForeignKey(m => m.DraftId);
    }
}

public class TenderVersionConfiguration : IEntityTypeConfiguration<TenderVersion>
{
    public void Configure(EntityTypeBuilder<TenderVersion> b)
    {
        b.ToTable("tender_versions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.DraftId).HasColumnName("draft_id");
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.Reason).HasColumnName("reason");
        b.Property(x => x.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb");
        b.Property(x => x.ContentHash).HasColumnName("content_hash");
        b.Property(x => x.PrevChainHash).HasColumnName("prev_chain_hash").HasDefaultValue("");
        b.Property(x => x.ChainHash).HasColumnName("chain_hash");
        b.Property(x => x.RuleSetVersion).HasColumnName("rule_set_version");
        b.Property(x => x.PublishedBy).HasColumnName("published_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.DraftId, x.Version }).IsUnique();
    }
}

public class CorrigendumConfiguration : IEntityTypeConfiguration<Corrigendum>
{
    public void Configure(EntityTypeBuilder<Corrigendum> b)
    {
        b.ToTable("corrigenda");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.DraftId).HasColumnName("draft_id");
        b.Property(x => x.VersionId).HasColumnName("version_id");
        b.Property(x => x.CorrigendumNo).HasColumnName("corrigendum_no");
        b.Property(x => x.Type).HasColumnName("type");
        b.Property(x => x.Reason).HasColumnName("reason");
        b.Property(x => x.Changes).HasColumnName("changes").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.NotifiedAt).HasColumnName("notified_at");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.DraftId, x.CorrigendumNo }).IsUnique();
    }
}

public class TenderCommitteeMemberConfiguration : IEntityTypeConfiguration<TenderCommitteeMember>
{
    public void Configure(EntityTypeBuilder<TenderCommitteeMember> b)
    {
        b.ToTable("tender_committee_members");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.DraftId).HasColumnName("draft_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Committee).HasColumnName("committee");
        b.Property(x => x.MemberName).HasColumnName("member_name");
        b.Property(x => x.Designation).HasColumnName("designation");
        b.Property(x => x.Email).HasColumnName("email");
        b.Property(x => x.IsChair).HasColumnName("is_chair");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.DraftId, x.Committee });
    }
}

public class TenderOwnershipConfiguration : IEntityTypeConfiguration<TenderOwnership>
{
    public void Configure(EntityTypeBuilder<TenderOwnership> b)
    {
        b.ToTable("tender_ownership");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.DraftId).HasColumnName("draft_id");
        b.Property(x => x.Relationship).HasColumnName("relationship").HasDefaultValue("owner");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.OrgId, x.DraftId }).IsUnique();

        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
        b.HasOne(x => x.Draft).WithMany().HasForeignKey(x => x.DraftId);
    }
}

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.EntityType).HasColumnName("entity_type");
        b.Property(x => x.EntityId).HasColumnName("entity_id");
        b.Property(x => x.Action).HasColumnName("action");
        b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.ActorName).HasColumnName("actor_name");
        b.Property(x => x.ActorRole).HasColumnName("actor_role");
        b.Property(x => x.Changes).HasColumnName("changes").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb");
        b.Property(x => x.IpAddress).HasColumnName("ip_address");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        b.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
        b.HasIndex(x => new { x.OrgId, x.CreatedAt });

        // No navigation to users: the actor reference is deliberately loose so the audit trail
        // outlives the account. See AuditEvent's remarks.
    }
}
