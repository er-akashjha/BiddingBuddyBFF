namespace BiddingBuddy.Bff.Core.DTOs.Capability;

/// <summary>
/// The org's capability profile as the Settings page reads it, plus a
/// <see cref="Completeness"/> block, because how much of this is filled in directly bounds how
/// much the fit engine can say. A half-empty profile does not produce a half-wrong verdict — it
/// produces <c>insufficient_data</c>, and the user needs to see why.
/// </summary>
public record CapabilityProfileDto(
    decimal? TurnoverFy1,
    decimal? TurnoverFy2,
    decimal? TurnoverFy3,
    string?  TurnoverFy1Label,
    decimal? NetWorth,
    DateOnly? IncorporationDate,
    string?  UdyamNumber,
    string?  UdyamCategory,
    string?  DpiitStartupNumber,
    string?  NsicNumber,
    string[] ServiceableStates,
    string[] CategoriesSupplied,
    decimal? EmdHeadroom,
    decimal? BgLimit,
    decimal? BgUtilised,
    DateTime? UpdatedAt,
    CompletenessDto Completeness);

/// <summary>
/// What the profile can and cannot yet answer. <see cref="MissingForFit"/> is deliberately
/// specific — "add your last completed FY turnover" is actionable, "profile 40% complete" is not.
/// </summary>
public record CompletenessDto(
    int Percent,
    /// <summary>Enough is present for the engine to return a real verdict rather than
    /// insufficient_data. The bar is deliberately low: turnover alone unlocks the single most
    /// common blocker in Indian public procurement.</summary>
    bool CanEvaluate,
    IReadOnlyList<string> MissingForFit);

public record UpdateCapabilityProfileDto(
    decimal? TurnoverFy1,
    decimal? TurnoverFy2,
    decimal? TurnoverFy3,
    string?  TurnoverFy1Label,
    decimal? NetWorth,
    DateOnly? IncorporationDate,
    string?  UdyamNumber,
    string?  UdyamCategory,
    string?  DpiitStartupNumber,
    string?  NsicNumber,
    string[]? ServiceableStates,
    string[]? CategoriesSupplied,
    decimal? EmdHeadroom,
    decimal? BgLimit,
    decimal? BgUtilised);

public record CredentialDto(
    Guid Id,
    string Kind,
    string Code,
    string? Label,
    string? Number,
    DateOnly? IssuedAt,
    DateOnly? ValidUntil,
    Guid? DocumentId,
    string? DocumentName,
    DateTime? VerifiedAt,
    string? Notes,
    /// <summary>Server-computed against today. The client shows it; it never derives it, so
    /// "expired" means one thing across web, mobile and the exported report.</summary>
    bool IsExpired,
    int? DaysUntilExpiry,
    DateTime CreatedAt);

public record UpsertCredentialDto(
    string Kind,
    string Code,
    string? Label,
    string? Number,
    DateOnly? IssuedAt,
    DateOnly? ValidUntil,
    Guid? DocumentId,
    string? Notes);

/// <summary>
/// A credential we think the org already holds, inferred from a file it has uploaded. Nothing is
/// written until the user accepts it — filling this profile should feel like confirming, not like
/// data entry, and an inferred certificate the user never saw would be exactly the kind of
/// unverified claim the fit engine must not treat as fact.
/// </summary>
public record CredentialSuggestionDto(
    string Kind,
    string Code,
    string Label,
    DateOnly? ValidUntil,
    Guid DocumentId,
    string DocumentName,
    /// <summary>Why we think so — shown verbatim so the guess is auditable by the user.</summary>
    string Because);
