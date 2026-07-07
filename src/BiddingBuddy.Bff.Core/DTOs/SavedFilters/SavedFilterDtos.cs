using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.DTOs.SavedFilters;

/// <summary>One saved filter set (either the last-used snapshot or a named view).</summary>
public record SavedFilterDto(
    Guid Id,
    string Kind,
    string? Name,
    SavedFilterState Filters,
    DateTime UpdatedAt
);

/// <summary>Combined payload for the Tenders page: the last-used snapshot plus named views.</summary>
public record SavedFiltersResponseDto(
    SavedFilterDto? LastUsed,
    IReadOnlyList<SavedFilterDto> Named
);

/// <summary>Body for upserting the last-used snapshot.</summary>
public record PutLastUsedFilterDto(
    SavedFilterState Filters
);

/// <summary>Body for creating a named view.</summary>
public record CreateNamedFilterDto(
    string Name,
    SavedFilterState Filters
);
