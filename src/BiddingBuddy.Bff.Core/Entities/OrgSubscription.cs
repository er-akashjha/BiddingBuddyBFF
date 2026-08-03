namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One row per org, mutated in place. `Status` is a display/dedup label — entitlement
/// resolution is date-driven in PlanService, so a stalled lifecycle worker can never
/// extend paid access.
/// </summary>
public class OrgSubscription
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string PlanCode { get; set; } = "free";
    public string Status { get; set; } = "active";   // trialing|active|past_due|expired|canceled
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? CurrentPeriodStart { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }

    // Lifecycle-worker email dedup stamps; reset by a successful renewal payment.
    public DateTime? TrialEndingNotifiedAt { get; set; }
    public DateTime? RenewalT14NotifiedAt { get; set; }
    public DateTime? RenewalT3NotifiedAt { get; set; }
    public DateTime? ExpiryNotifiedAt { get; set; }

    public string Provider { get; set; } = "razorpay";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
}
