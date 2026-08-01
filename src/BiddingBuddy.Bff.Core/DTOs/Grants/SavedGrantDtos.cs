namespace BiddingBuddy.Bff.Core.DTOs.Grants;

/// <summary>
/// Save / track a grant. The client sends the snapshot of the grant it already has on screen (from
/// the discovery list or detail), so the saved list needs no Mongo round-trip. <see cref="MongoGrantId"/>
/// is the grant's Mongo _id — the dedup key and the deep-link back to /grants/{id}.
/// </summary>
public record SaveGrantRequest(
    string MongoGrantId,
    string Title,
    string? AgencyName = null,
    string? OpportunityNumber = null,
    string? Category = null,
    DateOnly? CloseDate = null,
    decimal? AwardCeiling = null,
    string? Currency = null,
    bool IsForecast = false,
    string? SourceUrl = null,
    string? Note = null
);

public record SavedGrantDto(
    Guid Id,
    string MongoGrantId,
    string Title,
    string? AgencyName,
    string? OpportunityNumber,
    string? Category,
    DateOnly? CloseDate,
    decimal? AwardCeiling,
    string Currency,
    bool IsForecast,
    string? SourceUrl,
    string? Note,
    DateTime CreatedAt
);
