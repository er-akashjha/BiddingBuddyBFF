namespace BiddingBuddy.Bff.Core.Exceptions;

/// <summary>
/// A plan/entitlement limit refused an action inside a service. Carries the
/// machine-readable payload the SPA branches on (upgrade prompt vs generic error);
/// <c>GlobalExceptionHandler</c> serializes it as a coded 403 body — the same contract
/// <c>RequirePlanFeatureAttribute</c> emits for endpoint-level gates, so the client has
/// one shape to handle regardless of where the gate lives.
/// </summary>
public sealed class PlanLimitException(
    string code, string message, string feature,
    string? requiredPlan = null, string? currentPlan = null,
    int? used = null, int? limit = null) : Exception(message)
{
    public const string UpgradeRequired  = "UPGRADE_REQUIRED";
    public const string SeatLimitReached = "SEAT_LIMIT_REACHED";

    public string Code { get; } = code;
    public string Feature { get; } = feature;
    public string? RequiredPlan { get; } = requiredPlan;
    public string? CurrentPlan { get; } = currentPlan;
    public int? Used { get; } = used;
    public int? Limit { get; } = limit;
}
