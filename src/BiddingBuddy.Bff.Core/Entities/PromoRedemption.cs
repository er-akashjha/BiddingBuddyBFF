namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// One use per (code, org) — UNIQUE constraint. `IsLifetime` is copied from the code at
/// redemption time so a later edit to the code can't revoke (or grant) a lifetime
/// discount an org already earned.
/// </summary>
public class PromoRedemption
{
    public Guid Id { get; set; }
    public Guid PromoId { get; set; }
    public Guid OrgId { get; set; }
    public Guid? BillingPaymentId { get; set; }
    public bool IsLifetime { get; set; }
    public DateTime RedeemedAt { get; set; }

    public PromoCode PromoCode { get; set; } = default!;
}
