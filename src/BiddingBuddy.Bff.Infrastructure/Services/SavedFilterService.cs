using BiddingBuddy.Bff.Core.DTOs.SavedFilters;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class SavedFilterService(BffDbContext db) : ISavedFilterService
{
    // Cap named views so a user can't balloon the table.
    private const int MaxNamedViews = 50;

    public async Task<SavedFiltersResponseDto> GetAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var rows = await db.UserSavedFilters
            .Where(f => f.OrgId == orgId && f.UserId == userId)
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(ct);

        var lastUsed = rows.FirstOrDefault(f => f.Kind == SavedFilterKinds.LastUsed);
        var named = rows.Where(f => f.Kind == SavedFilterKinds.Named).Select(Map).ToList();

        return new SavedFiltersResponseDto(lastUsed is null ? null : Map(lastUsed), named);
    }

    public async Task<SavedFilterDto> UpsertLastUsedAsync(Guid orgId, Guid userId, SavedFilterState filters, CancellationToken ct = default)
    {
        var row = await db.UserSavedFilters
            .FirstOrDefaultAsync(f => f.OrgId == orgId && f.UserId == userId && f.Kind == SavedFilterKinds.LastUsed, ct);

        if (row is null)
        {
            row = new UserSavedFilter
            {
                OrgId = orgId,
                UserId = userId,
                Kind = SavedFilterKinds.LastUsed,
                Filters = filters,
            };
            db.UserSavedFilters.Add(row);
        }
        else
        {
            row.Filters = filters;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<SavedFilterDto> CreateNamedAsync(Guid orgId, Guid userId, string name, SavedFilterState filters, CancellationToken ct = default)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("A saved view needs a name.", nameof(name));

        var count = await db.UserSavedFilters
            .CountAsync(f => f.OrgId == orgId && f.UserId == userId && f.Kind == SavedFilterKinds.Named, ct);
        if (count >= MaxNamedViews)
            throw new InvalidOperationException($"You can save at most {MaxNamedViews} views.");

        var row = new UserSavedFilter
        {
            OrgId = orgId,
            UserId = userId,
            Kind = SavedFilterKinds.Named,
            Name = trimmed,
            Filters = filters,
        };
        db.UserSavedFilters.Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task DeleteAsync(Guid id, Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var row = await db.UserSavedFilters
            .FirstOrDefaultAsync(f => f.Id == id && f.OrgId == orgId && f.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Saved filter not found.");
        db.UserSavedFilters.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private static SavedFilterDto Map(UserSavedFilter f) =>
        new(f.Id, f.Kind, f.Name, f.Filters, f.UpdatedAt);
}
