namespace BiddingBuddy.Bff.Core.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public Guid OwnedBy { get; set; }
    public string Name { get; set; } = default!;
    public string? Slug { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public string? Industry { get; set; }
    public string? CompanySize { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Website { get; set; }
    public string? GemSellerId { get; set; }
    /// <summary>The company's seller name exactly as it appears on GeM bid result ladders. Set by the
    /// org so award results can resolve their bids to won/lost by matching the ladder. Optional —
    /// resolution falls back to the org <see cref="Name"/>.</summary>
    public string? GemSellerName { get; set; }
    public string? PrimaryCategory { get; set; }
    public string? LogoUrl { get; set; }

    /// <summary>
    /// supplier | buyer | both. Decides which half of the product an org sees.
    ///
    /// <para><c>both</c> exists because a PSU genuinely is both — it publishes tenders for its own
    /// procurement and bids for contracts elsewhere — and making it keep two workspaces would split
    /// its document vault and its team for no reason.</para>
    ///
    /// Defaults to <c>supplier</c>, which describes every organization that existed before
    /// migration 0031.
    /// </summary>
    public string OrgType { get; set; } = "supplier";

    // ── Procuring-entity identity (buyers only; NULL for every supplier) ─────
    // The §4.A fields that belong to the ORGANISATION rather than to any one notice. A department
    // retypes these on every tender today.

    /// <summary>central | state | psu | ulb | autonomous | cooperative | trust | private.</summary>
    public string? EntityType { get; set; }
    public string? Ministry { get; set; }
    public string? Department { get; set; }
    public string? Office { get; set; }

    /// <summary>The department's own organisation code, free text. Display only.</summary>
    public string? ProcuringEntityCode { get; set; }

    // ── Enterprise single sign-on (migration 0032) ──────────────────────────

    /// <summary>
    /// The Microsoft Entra directory this workspace belongs to. When set, a Microsoft sign-in whose
    /// <c>tid</c> matches joins this org automatically, with no invite and no join request.
    /// </summary>
    /// <remarks>
    /// Bound only by an owner/admin who has themselves signed in from that tenant
    /// (<c>OrganizationService.BindEntraTenantAsync</c>). Unique across orgs — one directory cannot
    /// feed two workspaces, or auto-join would be choosing someone's employer for them.
    /// </remarks>
    public Guid? EntraTenantId { get; set; }

    /// <summary>Role granted to an SSO auto-join. Defaults to the least-privileged role in the
    /// vocabulary, because an auto-join is the one membership no human approved.</summary>
    public string SsoDefaultRole { get; set; } = "viewer";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public User Owner { get; set; } = default!;
    public ICollection<OrgMember> Members { get; set; } = [];
    public ICollection<Bid> Bids { get; set; } = [];
    public ICollection<EmdPayment> EmdPayments { get; set; } = [];
    public ICollection<Invoice> Invoices { get; set; } = [];
    public ICollection<Competitor> Competitors { get; set; } = [];
}
