namespace BiddingBuddy.Bff.Core.Interfaces;

/// <param name="AlreadyUnlocked">True when this org unlocked this resource earlier in the
/// current IST month — the request is served without consuming quota.</param>
public sealed record AiQuotaVerdict(bool Allowed, bool AlreadyUnlocked, int Used, int? Quota);

/// <summary>
/// Per-org AI usage metering against the plan's monthly quota. Consume is idempotent per
/// (org, feature, resource, IST-month): re-viewing an unlocked resource is free.
/// </summary>
public interface IAiQuotaService
{
    Task<AiQuotaVerdict> TryConsumeAsync(
        Guid orgId, Guid? userId, string feature, string resourceId, CancellationToken ct = default);

    Task<(int Used, int? Quota)> GetUsageAsync(Guid orgId, string feature, CancellationToken ct = default);

    Task<bool> IsUnlockedAsync(Guid orgId, string feature, string resourceId, CancellationToken ct = default);

    /// <summary>
    /// Give the credit back. Deletes the usage row, which both decrements the month's count and
    /// re-arms the resource so the customer can try again.
    ///
    /// <para>Needed because the meter is taken before the work completes. A charge that survives
    /// a failed generation is the customer paying for our outage, and on the Free plan that is a
    /// third of their month. Returns true when a row was actually removed, so a double refund
    /// cannot manufacture credit.</para>
    /// </summary>
    Task<bool> RefundAsync(Guid orgId, string feature, string resourceId, CancellationToken ct = default);
}
