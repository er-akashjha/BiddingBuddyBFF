namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// A certificate, OEM authorization, statutory registration or empanelment the org holds.
///
/// <para>One table with a <see cref="Kind"/> discriminator rather than three, because every rule
/// that reads them asks the same two questions — do we hold it, and is it still valid on the bid
/// date. One table means one expiry scan.</para>
/// </summary>
public class OrgCredential
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>See <see cref="CredentialKinds"/>.</summary>
    public string Kind { get; set; } = default!;

    /// <summary>
    /// The matchable identity — a certification's standard ("ISO 9001:2015"), an OEM
    /// authorization's brand ("DELL"), a registration's scheme ("UDYAM"). Normalised to
    /// upper-case on write so matching against a tender's required list is case-stable.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>Human-facing name, free text. Never matched against.</summary>
    public string? Label { get; set; }

    public string? Number { get; set; }
    public DateOnly? IssuedAt { get; set; }

    /// <summary>Null = perpetual. The engine treats null as "does not lapse", NOT as "expired" —
    /// the common case for a statutory registration genuinely has no end date.</summary>
    public DateOnly? ValidUntil { get; set; }

    /// <summary>The vault document that proves it, so a finding can deep-link to the file
    /// instead of sending the user hunting. Nullable and ON DELETE SET NULL: deleting the PDF
    /// must not delete the claim.</summary>
    public Guid? DocumentId { get; set; }

    /// <summary>Reserved for a later verification step. Null = self-asserted, which is what
    /// every row is today — findings say so rather than implying we checked.</summary>
    public DateTime? VerifiedAt { get; set; }

    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public Document? Document { get; set; }

    /// <summary>True when this credential has lapsed, or will have lapsed, by <paramref name="on"/>.
    /// A certificate that expires the day before the bid closes is useless, which is why every
    /// rule asks about the DEADLINE date and not about today.</summary>
    public bool ExpiresBy(DateOnly on) => ValidUntil is not null && ValidUntil.Value < on;
}

/// <summary>The <see cref="OrgCredential.Kind"/> vocabulary. Constants, not an enum, to match the
/// rest of this codebase's string-discriminator convention (org roles, bid stages).</summary>
public static class CredentialKinds
{
    public const string Certification    = "certification";
    public const string OemAuthorization = "oem_authorization";
    public const string Registration     = "registration";
    public const string Empanelment      = "empanelment";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Certification, OemAuthorization, Registration, Empanelment,
    };
}
