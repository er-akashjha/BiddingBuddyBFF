namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Gives a newly created organization its opening <c>org_subscriptions</c> row.
///
/// <para>Exists as its own service because a workspace can be born in TWO places —
/// <c>OrganizationService.CreateAsync</c> (the onboarding form) and
/// <c>AuthService.CreateVerifiedAccountAsync</c> (password sign-up, which creates the org
/// inline the moment the e-mail OTP is confirmed). Seeding lived only in the first, so
/// every password sign-up silently landed on Free with no trial, breaking the "14-day
/// trial · no credit card" promise the marketing site makes in three places.</para>
///
/// Never throws: an org with no subscription row resolves to Free and still works, so a
/// seeding failure is a support ticket while a failed sign-up is a lost customer.
/// </summary>
public interface ISubscriptionSeeder
{
    /// <param name="startPlan">"free" to honour an explicit Free choice; anything else
    /// (including null) starts the default 14-day Growth trial.</param>
    Task SeedAsync(Guid orgId, string? startPlan, CancellationToken ct = default);
}
