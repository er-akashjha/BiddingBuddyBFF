using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class SavedGrantService(BffDbContext db) : ISavedGrantService
{
    public async Task<IReadOnlyList<SavedGrantDto>> ListAsync(Guid orgId, CancellationToken ct = default)
    {
        var rows = await db.SavedGrants
            .Where(s => s.OrgId == orgId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<string>> ListIdsAsync(Guid orgId, CancellationToken ct = default)
        => await db.SavedGrants
            .Where(s => s.OrgId == orgId)
            .Select(s => s.MongoGrantId)
            .ToListAsync(ct);

    public async Task<SavedGrantDto> SaveAsync(Guid orgId, Guid userId, SaveGrantRequest dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.MongoGrantId))
            throw new ArgumentException("mongoGrantId is required.", nameof(dto));
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("title is required.", nameof(dto));

        // Idempotent: re-saving refreshes the snapshot (a stale title/deadline can be corrected)
        // rather than erroring or duplicating.
        var existing = await db.SavedGrants
            .FirstOrDefaultAsync(s => s.OrgId == orgId && s.MongoGrantId == dto.MongoGrantId, ct);
        if (existing is not null)
        {
            ApplySnapshot(existing, dto);
            await db.SaveChangesAsync(ct);
            return Map(existing);
        }

        var saved = new SavedGrant
        {
            OrgId = orgId,
            MongoGrantId = dto.MongoGrantId,
            SavedBy = userId,
        };
        ApplySnapshot(saved, dto);
        db.SavedGrants.Add(saved);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race on the unique (org_id, mongo_grant_id) index — return the row that won.
            db.Entry(saved).State = EntityState.Detached;
            var raced = await db.SavedGrants
                .FirstOrDefaultAsync(s => s.OrgId == orgId && s.MongoGrantId == dto.MongoGrantId, ct);
            if (raced is null) throw;
            return Map(raced);
        }

        return Map(saved);
    }

    public async Task UnsaveAsync(Guid orgId, string mongoGrantId, CancellationToken ct = default)
    {
        var row = await db.SavedGrants
            .FirstOrDefaultAsync(s => s.OrgId == orgId && s.MongoGrantId == mongoGrantId, ct);
        if (row is null) return; // idempotent — unsaving something not saved is fine
        db.SavedGrants.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    private static void ApplySnapshot(SavedGrant s, SaveGrantRequest dto)
    {
        s.Title = dto.Title;
        s.AgencyName = dto.AgencyName;
        s.OpportunityNumber = dto.OpportunityNumber;
        s.Category = dto.Category;
        s.CloseDate = dto.CloseDate;
        s.AwardCeiling = dto.AwardCeiling;
        s.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency!;
        s.IsForecast = dto.IsForecast;
        s.SourceUrl = dto.SourceUrl;
        if (dto.Note is not null) s.Note = dto.Note;
    }

    private static SavedGrantDto Map(SavedGrant s) => new(
        s.Id, s.MongoGrantId, s.Title, s.AgencyName, s.OpportunityNumber, s.Category,
        s.CloseDate, s.AwardCeiling, s.Currency, s.IsForecast, s.SourceUrl, s.Note, s.CreatedAt);
}
