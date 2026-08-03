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
}
