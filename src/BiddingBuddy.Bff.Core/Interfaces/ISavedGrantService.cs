using BiddingBuddy.Bff.Core.DTOs.Grants;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface ISavedGrantService
{
    Task<IReadOnlyList<SavedGrantDto>> ListAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>Just the saved grants' Mongo ids — lets the discovery list mark saved rows in one cheap call.</summary>
    Task<IReadOnlyList<string>> ListIdsAsync(Guid orgId, CancellationToken ct = default);

    /// <summary>Idempotent save (upsert on org + grant); refreshes the snapshot on re-save. Returns the row.</summary>
    Task<SavedGrantDto> SaveAsync(Guid orgId, Guid userId, SaveGrantRequest dto, CancellationToken ct = default);

    /// <summary>Remove a saved grant by its Mongo id. Idempotent — a no-op if it wasn't saved.</summary>
    Task UnsaveAsync(Guid orgId, string mongoGrantId, CancellationToken ct = default);
}
