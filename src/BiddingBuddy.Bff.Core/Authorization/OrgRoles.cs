namespace BiddingBuddy.Bff.Core.Authorization;

/// <summary>
/// The organization role vocabulary and what each role may do.
///
/// Until now this system had no authorization enforcement point at all: <c>OrgContextMiddleware</c>
/// checks membership and never reads the role, and the single real role check lived private to
/// <c>OrganizationService</c>. Buyer-side tendering cannot ship on that, because the GePNIC
/// separation-of-duties model (docs/gov-tendering/PLAN.md §2.3, §4.I) is the control that makes a
/// department's file auditable — an unenforced separation is worse than none, since it is recorded
/// in the audit file as though it held.
///
/// Scope note: the mechanism here is general and reusable, but it is currently applied ONLY to
/// buyer routes. Every pre-existing endpoint keeps its membership-only behaviour, so nothing that
/// works today starts 403-ing. Retrofitting the rest of the API is a separate, reviewable change.
/// </summary>
public static class OrgRoles
{
    // ── Supplier-side (pre-existing; unchanged) ──────────────────────────────
    public const string Owner      = "owner";
    public const string Admin      = "admin";
    public const string BidManager = "bid_manager";
    public const string Finance    = "finance";
    public const string Sales      = "sales";
    public const string Viewer     = "viewer";

    // ── Buyer-side (new in migration 0031) ──────────────────────────────────
    /// <summary>Creates and edits tender drafts. GePNIC "PO Admin".</summary>
    public const string PoAdmin     = "po_admin";
    /// <summary>Publishes drafts and issues corrigenda. GePNIC "PO Publisher".</summary>
    public const string PoPublisher = "po_publisher";
    /// <summary>Opens the electronic tender box. Documentary in Phase 1 — nothing to open until Phase 3.</summary>
    public const string PoOpener    = "po_opener";
    /// <summary>Evaluates bids. Documentary in Phase 1, for the same reason.</summary>
    public const string PoEvaluator = "po_evaluator";
    /// <summary>Read-only, full visibility, including the audit file. Cannot change anything.</summary>
    public const string Auditor     = "auditor";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Owner, Admin, BidManager, Finance, Sales, Viewer,
        PoAdmin, PoPublisher, PoOpener, PoEvaluator, Auditor,
    };

    /// <summary>Roles that only make sense inside a buyer organization.</summary>
    public static readonly IReadOnlySet<string> BuyerRoles = new HashSet<string>(StringComparer.Ordinal)
    {
        PoAdmin, PoPublisher, PoOpener, PoEvaluator, Auditor,
    };
}

/// <summary>
/// A capability is what an endpoint actually requires. Endpoints name capabilities, never roles,
/// so that adding a role later is one edit to <see cref="OrgCapabilities.Grants"/> instead of a
/// sweep over every controller — the mistake that makes role checks rot in most codebases.
/// </summary>
public static class OrgCapabilities
{
    public const string TenderAuthor   = "tender.author";    // create/edit/delete a draft
    public const string TenderPublish  = "tender.publish";   // publish, corrigendum, cancel, award
    public const string TenderRead     = "tender.read";      // see drafts + the audit file
    public const string CommitteeManage = "committee.manage"; // name the opening/evaluation committees
    public const string BillingManage  = "billing.manage";   // checkout, verify payment, payment history

    /// <summary>
    /// Role → capabilities. <c>owner</c> and <c>admin</c> hold everything: a workspace whose owner
    /// can be locked out of their own tenders is a support ticket, not a security control. The
    /// separation of duties that matters is recorded and surfaced (see the self-publication warning
    /// in the compliance engine) rather than enforced into a dead end for a one-officer department.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Grants =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [OrgRoles.Owner]       = Set(TenderAuthor, TenderPublish, TenderRead, CommitteeManage, BillingManage),
            [OrgRoles.Admin]       = Set(TenderAuthor, TenderPublish, TenderRead, CommitteeManage, BillingManage),

            [OrgRoles.PoAdmin]     = Set(TenderAuthor, TenderRead, CommitteeManage),
            [OrgRoles.PoPublisher] = Set(TenderPublish, TenderRead),

            // Opening and evaluation are Phase 3 duties. In Phase 1 these roles exist so committee
            // membership is meaningful when that lands; they read, they do not author.
            [OrgRoles.PoOpener]    = Set(TenderRead),
            [OrgRoles.PoEvaluator] = Set(TenderRead),
            [OrgRoles.Auditor]     = Set(TenderRead),

            // Supplier-side roles hold nothing on the buyer surface. A bid manager at a supplier
            // org has no business authoring a department's notice, and 'both'-type orgs get the
            // buyer roles assigned explicitly.
            [OrgRoles.BidManager]  = Set(),
            [OrgRoles.Finance]     = Set(),
            [OrgRoles.Sales]       = Set(),
            [OrgRoles.Viewer]      = Set(),
        };

    public static bool Has(string? role, string capability)
        => role is not null
        && Grants.TryGetValue(role, out var caps)
        && caps.Contains(capability);

    private static IReadOnlySet<string> Set(params string[] caps)
        => new HashSet<string>(caps, StringComparer.Ordinal);
}
