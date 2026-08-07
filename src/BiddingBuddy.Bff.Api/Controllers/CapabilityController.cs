using BiddingBuddy.Bff.Core.DTOs.Capability;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// The org's capability profile — turnover, registrations, credentials, reach and EMD headroom.
///
/// <para>This is the operand every "can we bid this?" question was missing. The enrichment
/// pipeline produces one artifact per tender, globally; nothing in it knows who is asking. With
/// these rows present, <c>/api/analysis/tenders/{id}/fit</c> can answer with arithmetic instead
/// of prose.</para>
///
/// <para>Membership-only, like every non-buyer route in this API. Deliberate: a bid manager who
/// discovers the turnover figure is a year stale needs to fix it in the moment, and gating that
/// behind an owner would mean the profile silently rots instead.</para>
/// </summary>
[ApiController]
[Route("api/capability")]
[Authorize]
[Produces("application/json")]
public class CapabilityController(ICapabilityProfileService capability) : BffControllerBase
{
    /// <summary>
    /// The org's profile. Always 200 — an org that has never filled this in gets an empty
    /// profile whose <c>completeness.missingForFit</c> names what to add, rather than a 404 the
    /// client would have to translate.
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(CapabilityProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken ct)
        => Ok(await capability.GetAsync(CurrentOrgId, ct));

    /// <summary>
    /// Replace the profile. PUT, not PATCH: this is one form, and null means "cleared". Patch
    /// semantics would leave a wrong turnover figure un-erasable from the UI, and a stale
    /// turnover is worse than an absent one — absent yields "we can't say", stale yields a
    /// confident wrong verdict.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(CapabilityProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateCapabilityProfileDto dto, CancellationToken ct)
        => Ok(await capability.UpdateAsync(CurrentOrgId, CurrentUserId, dto, ct));

    /// <summary>Certificates, OEM letters, registrations and empanelments the org holds.</summary>
    [HttpGet("credentials")]
    [ProducesResponseType(typeof(IReadOnlyList<CredentialDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCredentials(CancellationToken ct)
        => Ok(await capability.ListCredentialsAsync(CurrentOrgId, ct));

    /// <summary>Add or update one. Upserts on (kind, code), so re-adding a standard refreshes
    /// its expiry rather than creating a duplicate a rule would have to choose between.</summary>
    [HttpPost("credentials")]
    [ProducesResponseType(typeof(CredentialDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertCredential(
        [FromBody] UpsertCredentialDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await capability.UpsertCredentialAsync(CurrentOrgId, CurrentUserId, dto, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, code = "INVALID_CREDENTIAL" });
        }
    }

    [HttpDelete("credentials/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCredential(Guid id, CancellationToken ct)
    {
        await capability.DeleteCredentialAsync(CurrentOrgId, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Credentials we think the org already holds, inferred from files already in its vault.
    /// Read-only: nothing is recorded until the user accepts one. Filling this profile should
    /// feel like confirming rather than like data entry — but an inferred certificate the user
    /// never saw would be exactly the unverified claim the fit engine must not treat as fact.
    /// </summary>
    [HttpGet("credential-suggestions")]
    [ProducesResponseType(typeof(IReadOnlyList<CredentialSuggestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SuggestCredentials(CancellationToken ct)
        => Ok(await capability.SuggestFromDocumentsAsync(CurrentOrgId, ct));
}
