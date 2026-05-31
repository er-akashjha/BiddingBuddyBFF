using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Competitors;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class CompetitorService(BffDbContext db) : ICompetitorService
{
    public async Task<PagedResult<CompetitorDto>> ListAsync(
        Guid orgId, string? threatLevel, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Competitors.Where(c => c.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(threatLevel))
            query = query.Where(c => c.ThreatLevel == threatLevel);

        var total = await query.CountAsync(ct);
        var pg = Math.Max(1, page);
        var sz = Math.Clamp(pageSize, 1, 100);

        var items = await query
            .OrderByDescending(c => c.TotalContracts)
            .Skip((pg - 1) * sz).Take(sz)
            .Select(c => new CompetitorDto(
                c.Id, c.CompanyName, c.GemSellerId, c.Tier, c.ThreatLevel,
                c.WinRate, c.TotalContracts, c.TotalWinValue, c.AvgBidValue,
                c.ActiveStates, c.ActiveCategories, c.FirstSeenAt, c.LastSeenAt, c.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<CompetitorDto>(items, total, pg, sz);
    }

    public async Task<CompetitorDetailDto> GetAsync(Guid competitorId, Guid orgId, CancellationToken ct = default)
    {
        var c = await db.Competitors
            .Include(x => x.BidObservations)
            .FirstOrDefaultAsync(x => x.Id == competitorId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Competitor not found.");

        var obs = c.BidObservations
            .OrderByDescending(o => o.ObservedDate)
            .Take(20)
            .Select(o => new BidObservationDto(
                o.Id, o.TenderId, o.GemTenderId, null,
                o.ObservedBidValue, o.WasWinner, null, o.ObservedDate ?? DateOnly.MinValue))
            .ToList();

        return new CompetitorDetailDto(
            c.Id, c.CompanyName, c.GemSellerId, c.Tier, c.ThreatLevel,
            c.WinRate, c.TotalContracts, c.TotalWinValue, c.AvgBidValue,
            c.ActiveStates, c.ActiveCategories, c.FirstSeenAt, c.LastSeenAt, c.UpdatedAt, obs);
    }

    public async Task<CompetitorDto> UpdateAsync(Guid competitorId, Guid orgId, UpdateCompetitorDto dto, CancellationToken ct = default)
    {
        var c = await db.Competitors
            .FirstOrDefaultAsync(x => x.Id == competitorId && x.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Competitor not found.");

        if (dto.Tier         is not null) c.Tier        = dto.Tier;
        if (dto.ThreatLevel  is not null) c.ThreatLevel = dto.ThreatLevel;

        await db.SaveChangesAsync(ct);
        return new CompetitorDto(
            c.Id, c.CompanyName, c.GemSellerId, c.Tier, c.ThreatLevel,
            c.WinRate, c.TotalContracts, c.TotalWinValue, c.AvgBidValue,
            c.ActiveStates, c.ActiveCategories, c.FirstSeenAt, c.LastSeenAt, c.UpdatedAt);
    }

    public async Task<IReadOnlyList<BidObservationDto>> GetObservationsAsync(
        Guid competitorId, Guid orgId, int limit, CancellationToken ct = default)
    {
        var exists = await db.Competitors.AnyAsync(c => c.Id == competitorId && c.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Competitor not found.");

        return await db.CompetitorBidObservations
            .Where(o => o.CompetitorId == competitorId)
            .OrderByDescending(o => o.ObservedDate)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(o => new BidObservationDto(
                o.Id, o.TenderId, o.GemTenderId, null,
                o.ObservedBidValue, o.WasWinner, null, o.ObservedDate ?? DateOnly.MinValue))
            .ToListAsync(ct);
    }
}
