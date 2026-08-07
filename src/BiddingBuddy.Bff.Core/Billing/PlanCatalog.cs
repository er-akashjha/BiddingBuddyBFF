namespace BiddingBuddy.Bff.Core.Billing;

/// <summary>
/// Gateable feature names. Endpoints and services reference these constants, never plan
/// codes — the same rule as <see cref="Authorization.OrgCapabilities"/>: which plans hold a
/// feature is one edit to <see cref="PlanCatalog"/>, not a sweep over controllers.
/// </summary>
public static class PlanFeatures
{
    /// <summary>Metered per month via ai_usage_records, not a boolean gate.</summary>
    public const string AiSummary = "ai_summary";
    /// <summary>Competitor intelligence + tender result/award history.</summary>
    public const string Competitors = "competitors";
    /// <summary>The AI "can I bid?" eligibility verdict on tender analysis.</summary>
    public const string EligibilityCheck = "eligibility_check";
    public const string Workflows = "workflows";
    /// <summary>Capped per plan (SavedFilterCap), named here for 403 payloads.</summary>
    public const string SavedFilters = "saved_filters";
    /// <summary>Named for 403 payloads on invite/approve; the cap itself is SeatCap.</summary>
    public const string Seats = "seats";
}

public sealed record PlanDefinition(
    string Code,
    string Name,
    /// <summary>Annual price in paise (Razorpay's unit). 0 = free.</summary>
    long PricePaiseAnnual,
    /// <summary>Struck-through "launch pricing" anchor, paise. 0 = none shown.</summary>
    long AnchorPricePaiseAnnual,
    int SeatCap,
    /// <summary>Null = unlimited (fair use — usage is still recorded).</summary>
    int? AiSummariesPerMonth,
    /// <summary>Null = unlimited.</summary>
    int? SavedFilterCap,
    /// <summary>Minimum alert-digest cooldown the plan allows. 0 = real-time.</summary>
    int AlertFloorMinutes,
    IReadOnlySet<string> Features,
    bool IsPopular,
    string Tagline,
    IReadOnlyList<string> Bullets);

/// <summary>
/// The plan catalog — prices, quotas, feature grants. Code, not DB rows, for the same
/// reason OrgRoles.cs holds the capability map: every gate compiles against these
/// constants, and a price change is a reviewed one-line PR. Per-org exceptions live in
/// org_entitlement_overrides and are merged over this by PlanService.
/// Serialized to the SPA via GET /api/public/plans.
/// </summary>
public static class PlanCatalog
{
    public const string Free    = "free";
    public const string Starter = "starter";
    public const string Growth  = "growth";
    public const string Pro     = "pro";

    public static readonly IReadOnlyList<PlanDefinition> All = new[]
    {
        new PlanDefinition(Free, "Free",
            PricePaiseAnnual: 0, AnchorPricePaiseAnnual: 0,
            SeatCap: 1, AiSummariesPerMonth: 3, SavedFilterCap: 1,
            AlertFloorMinutes: 10080, // weekly
            Features: Set(),
            IsPopular: false,
            Tagline: "Search every tender, taste the AI",
            Bullets: new[]
            {
                "Search & browse every GeM, eProcure and Coal India tender",
                "Full tender detail, documents and PDF downloads",
                "Bid pipeline with deadline tracking",
                "Document vault & EMD register",
                "3 AI tender summaries a month",
                "Weekly email alerts on 1 saved interest",
                "1 user",
            }),

        new PlanDefinition(Starter, "Starter",
            PricePaiseAnnual: 299_900, AnchorPricePaiseAnnual: 600_000,
            SeatCap: 1, AiSummariesPerMonth: 30, SavedFilterCap: null,
            AlertFloorMinutes: 1440, // daily
            Features: Set(),
            IsPopular: false,
            Tagline: "For the solo MSME owner",
            Bullets: new[]
            {
                "Everything in Free, plus:",
                "Daily alerts instead of weekly",
                "30 AI tender summaries a month — 10× Free",
                "Unlimited saved interests & filters",
                "Email support",
                "1 user",
            }),

        new PlanDefinition(Growth, "Growth",
            PricePaiseAnnual: 1_199_900, AnchorPricePaiseAnnual: 2_400_000,
            SeatCap: 5, AiSummariesPerMonth: 300, SavedFilterCap: null,
            AlertFloorMinutes: 0, // real-time
            Features: Set(PlanFeatures.Competitors, PlanFeatures.EligibilityCheck),
            IsPopular: true,
            Tagline: "For small bidding teams",
            Bullets: new[]
            {
                "Everything in Starter, plus:",
                "Real-time alerts the moment a tender is published",
                "300 AI summaries a month",
                "AI eligibility check — \"can we actually bid?\"",
                "Competitor intelligence & win/loss history",
                "Award results & price-to-win ladders",
                "5 users",
                "Priority support",
            }),

        new PlanDefinition(Pro, "Pro",
            PricePaiseAnnual: 2_999_900, AnchorPricePaiseAnnual: 6_000_000,
            SeatCap: 15, AiSummariesPerMonth: null, SavedFilterCap: null,
            AlertFloorMinutes: 0,
            Features: Set(PlanFeatures.Competitors, PlanFeatures.EligibilityCheck, PlanFeatures.Workflows),
            IsPopular: false,
            Tagline: "For consultants & active contractors",
            Bullets: new[]
            {
                "Everything in Growth, plus:",
                "Unlimited AI summaries & eligibility checks (fair use)",
                "15 users",
                "Dedicated account manager",
                "Onboarding & bid-strategy session",
            }),
    };

    public static readonly IReadOnlyDictionary<string, PlanDefinition> ByCode =
        All.ToDictionary(p => p.Code, StringComparer.Ordinal);

    public static PlanDefinition Get(string code) =>
        ByCode.TryGetValue(code, out var p) ? p : ByCode[Free];

    public static bool IsPaid(string code) => Get(code).PricePaiseAnnual > 0;

    /// <summary>Cheapest plan holding a boolean feature — the "requiredPlan" in 403 payloads.</summary>
    public static string? LowestPlanWith(string feature) =>
        All.FirstOrDefault(p => p.Features.Contains(feature))?.Code;

    /// <summary>The next tier up, for quota-exhausted upgrade prompts. Null when already at the top.</summary>
    public static string? NextPlanUp(string code)
    {
        for (var i = 0; i < All.Count - 1; i++)
            if (All[i].Code == code) return All[i + 1].Code;
        return null;
    }

    private static IReadOnlySet<string> Set(params string[] features)
        => new HashSet<string>(features, StringComparer.Ordinal);
}
