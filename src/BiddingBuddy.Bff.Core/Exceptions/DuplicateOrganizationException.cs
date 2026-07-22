using BiddingBuddy.Bff.Core.DTOs.Orgs;

namespace BiddingBuddy.Bff.Core.Exceptions;

/// <summary>
/// Organization creation was refused because the company already has a workspace.
/// Carries the payload the client needs to offer "request to join" instead of a dead
/// end — which is why this is its own type rather than an
/// <see cref="InvalidOperationException"/> with a magic message string.
///
/// <para><c>OrganizationsController</c> maps it to <b>409 Conflict</b>. It deliberately
/// does NOT flow to <c>GlobalExceptionHandler</c>: that would render it as a bare
/// ProblemDetails and drop <see cref="Conflict"/>, leaving the SPA able to see
/// "conflict" but not which organization it conflicted with.</para>
/// </summary>
public sealed class DuplicateOrganizationException(OrgExistsDto conflict)
    : Exception($"An organization matching this {conflict.Match} already exists.")
{
    public OrgExistsDto Conflict { get; } = conflict;
}
