using BiddingBuddy.Bff.Core.DTOs.Capability;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Reads and writes the org's capability profile and credentials — the operands the fit engine
/// evaluates a tender against.
/// </summary>
public interface ICapabilityProfileService
{
    /// <summary>Never null: an org with no row gets an empty profile whose completeness reports
    /// what is missing. Returning null here would push "profile absent" branching into every
    /// caller and, eventually, into a rule that read absent as zero.</summary>
    Task<CapabilityProfileDto> GetAsync(Guid orgId, CancellationToken ct = default);

    Task<CapabilityProfileDto> UpdateAsync(
        Guid orgId, Guid userId, UpdateCapabilityProfileDto dto, CancellationToken ct = default);

    Task<IReadOnlyList<CredentialDto>> ListCredentialsAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>Upsert on (org, kind, code) — re-adding "ISO 9001:2015" updates its expiry
    /// rather than creating a second row a rule would have to choose between.</summary>
    Task<CredentialDto> UpsertCredentialAsync(
        Guid orgId, Guid userId, UpsertCredentialDto dto, CancellationToken ct = default);

    Task DeleteCredentialAsync(Guid orgId, Guid credentialId, CancellationToken ct = default);

    /// <summary>Credentials inferable from files already in the org's vault, excluding any it
    /// has already recorded. Read-only — nothing is written until the user accepts.</summary>
    Task<IReadOnlyList<CredentialSuggestionDto>> SuggestFromDocumentsAsync(
        Guid orgId, CancellationToken ct = default);

    /// <summary>The raw operands, for the fit engine. Separate from <see cref="GetAsync"/>
    /// because the engine needs entities (expiry maths, exact codes), not display DTOs.</summary>
    Task<(OrgCapabilityProfile? Profile, IReadOnlyList<OrgCredential> Credentials)> GetOperandsAsync(
        Guid orgId, CancellationToken ct = default);
}
