using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BiddingBuddy.Bff.Infrastructure.Persistence.Configurations;

public class OrgSubscriptionConfiguration : IEntityTypeConfiguration<OrgSubscription>
{
    public void Configure(EntityTypeBuilder<OrgSubscription> b)
    {
        b.ToTable("org_subscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.PlanCode).HasColumnName("plan_code").HasDefaultValue("free");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("active");
        b.Property(x => x.TrialEndsAt).HasColumnName("trial_ends_at");
        b.Property(x => x.CurrentPeriodStart).HasColumnName("current_period_start");
        b.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
        b.Property(x => x.TrialEndingNotifiedAt).HasColumnName("trial_ending_notified_at");
        b.Property(x => x.RenewalT14NotifiedAt).HasColumnName("renewal_t14_notified_at");
        b.Property(x => x.RenewalT3NotifiedAt).HasColumnName("renewal_t3_notified_at");
        b.Property(x => x.ExpiryNotifiedAt).HasColumnName("expiry_notified_at");
        b.Property(x => x.Provider).HasColumnName("provider").HasDefaultValue("razorpay");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(x => x.OrgId).IsUnique();
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class BillingPaymentConfiguration : IEntityTypeConfiguration<BillingPayment>
{
    public void Configure(EntityTypeBuilder<BillingPayment> b)
    {
        b.ToTable("billing_payments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.PlanCode).HasColumnName("plan_code");
        b.Property(x => x.Cycle).HasColumnName("cycle").HasDefaultValue("annual");
        b.Property(x => x.AmountPaise).HasColumnName("amount_paise");
        b.Property(x => x.Currency).HasColumnName("currency").HasDefaultValue("INR");
        b.Property(x => x.PromoCodeId).HasColumnName("promo_code_id");
        b.Property(x => x.OriginalAmountPaise).HasColumnName("original_amount_paise");
        b.Property(x => x.DiscountPaise).HasColumnName("discount_paise");
        b.Property(x => x.RazorpayOrderId).HasColumnName("razorpay_order_id");
        b.Property(x => x.RazorpayPaymentId).HasColumnName("razorpay_payment_id");
        b.Property(x => x.Status).HasColumnName("status").HasDefaultValue("created");
        b.Property(x => x.SignatureVerified).HasColumnName("signature_verified").HasDefaultValue(false);
        b.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.CapturedAt).HasColumnName("captured_at");

        b.HasIndex(x => x.RazorpayOrderId).IsUnique();
        b.HasIndex(x => new { x.OrgId, x.CreatedAt });
        b.HasOne(x => x.Organization).WithMany().HasForeignKey(x => x.OrgId);
    }
}

public class BillingWebhookEventConfiguration : IEntityTypeConfiguration<BillingWebhookEvent>
{
    public void Configure(EntityTypeBuilder<BillingWebhookEvent> b)
    {
        b.ToTable("billing_webhook_events");
        b.HasKey(x => x.EventId);
        b.Property(x => x.EventId).HasColumnName("event_id");
        b.Property(x => x.EventType).HasColumnName("event_type");
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        b.Property(x => x.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("now()");
        b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
    }
}

public class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> b)
    {
        b.ToTable("ai_usage_records");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.Feature).HasColumnName("feature");
        b.Property(x => x.ResourceId).HasColumnName("resource_id");
        b.Property(x => x.PeriodMonth).HasColumnName("period_month").HasColumnType("date");
        b.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");

        b.HasIndex(x => new { x.OrgId, x.Feature, x.ResourceId, x.PeriodMonth }).IsUnique();
        b.HasIndex(x => new { x.OrgId, x.Feature, x.PeriodMonth });
    }
}

public class OrgEntitlementOverrideConfiguration : IEntityTypeConfiguration<OrgEntitlementOverride>
{
    public void Configure(EntityTypeBuilder<OrgEntitlementOverride> b)
    {
        b.ToTable("org_entitlement_overrides");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.Key).HasColumnName("key");
        b.Property(x => x.Value).HasColumnName("value");
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        b.HasIndex(x => new { x.OrgId, x.Key }).IsUnique();
    }
}

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> b)
    {
        b.ToTable("promo_codes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Code).HasColumnName("code");
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.DiscountType).HasColumnName("discount_type");
        b.Property(x => x.DiscountValue).HasColumnName("discount_value");
        b.Property(x => x.AppliesToPlans).HasColumnName("applies_to_plans").HasColumnType("text[]");
        b.Property(x => x.MaxRedemptions).HasColumnName("max_redemptions");
        b.Property(x => x.RedeemedCount).HasColumnName("redeemed_count").HasDefaultValue(0);
        b.Property(x => x.IsLifetime).HasColumnName("is_lifetime").HasDefaultValue(false);
        b.Property(x => x.ValidFrom).HasColumnName("valid_from");
        b.Property(x => x.ValidUntil).HasColumnName("valid_until");
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class PromoRedemptionConfiguration : IEntityTypeConfiguration<PromoRedemption>
{
    public void Configure(EntityTypeBuilder<PromoRedemption> b)
    {
        b.ToTable("promo_redemptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.PromoId).HasColumnName("promo_id");
        b.Property(x => x.OrgId).HasColumnName("org_id");
        b.Property(x => x.BillingPaymentId).HasColumnName("billing_payment_id");
        b.Property(x => x.IsLifetime).HasColumnName("is_lifetime").HasDefaultValue(false);
        b.Property(x => x.RedeemedAt).HasColumnName("redeemed_at").HasDefaultValueSql("now()");

        b.HasIndex(x => new { x.PromoId, x.OrgId }).IsUnique();
        b.HasOne(x => x.PromoCode).WithMany().HasForeignKey(x => x.PromoId);
    }
}
