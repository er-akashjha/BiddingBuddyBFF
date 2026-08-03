namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// An email domain that should be routed to an organization's identity provider instead of being
/// shown a password box.
/// </summary>
/// <remarks>
/// <b>This grants nothing.</b> It answers "which sign-in button do we press for this person", and a
/// wrong answer is harmless: the user is sent to Microsoft, Microsoft states who they actually are,
/// and if their <c>tid</c> does not match an org's <see cref="Organization.EntraTenantId"/> they join
/// nothing. Membership is decided by the signed token, never by the domain.
///
/// <para>Rows are written by the server when a tenant-matched user signs in — never typed in by a
/// customer. That is what lets this exist without a DNS-verification flow of our own: Entra will not
/// attach a custom domain to a directory until someone proves ownership with a DNS TXT record, so by
/// the time a work account's address reaches us, Microsoft has already done the verification.</para>
/// </remarks>
public class OrgSsoDomain
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }

    /// <summary>Bare domain, lower-cased (<c>acme.com</c>). Unique across all orgs.</summary>
    public string Domain { get; set; } = default!;

    /// <summary><c>entra</c> — observed from a tenant-matched sign-in. <c>manual</c> — reserved for a
    /// future admin-entered, separately-verified domain; nothing writes it yet.</summary>
    public string Source { get; set; } = "entra";

    public DateTime CreatedAt { get; set; }

    public Organization Org { get; set; } = default!;
}
