using BiddingBuddy.Bff.Core.DTOs.SavedFilters;
using BiddingBuddy.Bff.Core.Entities;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ISavedFilterService
{
    /// <summary>The user's last-used snapshot (if any) plus their named views for this org.</summary>
    Task<SavedFiltersResponseDto> GetAsync(Guid orgId, Guid userId, CancellationToken ct = default);

    /// <summary>Upsert the single "last_used" snapshot for this (user, org).</summary>
    Task<SavedFilterDto> UpsertLastUsedAsync(Guid orgId, Guid userId, SavedFilterState filters, CancellationToken ct = default);

    /// <summary>Create a named view.</summary>
    Task<SavedFilterDto> CreateNamedAsync(Guid orgId, Guid userId, string name, SavedFilterState filters, CancellationToken ct = default);

    /// <summary>Delete one of the user's saved filters (named or last-used) by id.</summary>
    Task DeleteAsync(Guid id, Guid orgId, Guid userId, CancellationToken ct = default);
}
