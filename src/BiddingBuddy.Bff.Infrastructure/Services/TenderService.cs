using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class TenderService(BffDbContext db) : ITenderService
{
    public async Task<PagedResult<TenderListItemDto>> ListAsync(Guid orgId, TenderFiltersDto f, CancellationToken ct = default)
    {
        var query = db.Tenders.AsQueryable();

        if (!string.IsNullOrWhiteSpace(f.Search))
            query = query.Where(t =>
                t.Title.Contains(f.Search) ||
                t.GemTenderId.Contains(f.Search) ||
                (t.BuyerOrgName != null && t.BuyerOrgName.Contains(f.Search)));

        if (!string.IsNullOrWhiteSpace(f.Category))
            query = query.Where(t => t.Category == f.Category);
        if (!string.IsNullOrWhiteSpace(f.State))
            query = query.Where(t => t.State == f.State);
        if (!string.IsNullOrWhiteSpace(f.Status))
            query = query.Where(t => t.Status == f.Status);
        if (f.MinValue.HasValue)
            query = query.Where(t => t.TenderValue >= f.MinValue);
        if (f.MaxValue.HasValue)
            query = query.Where(t => t.TenderValue <= f.MaxValue);
        if (f.ClosingAfter.HasValue)
            query = query.Where(t => t.ClosingDate >= f.ClosingAfter);
        if (f.ClosingBefore.HasValue)
            query = query.Where(t => t.ClosingDate <= f.ClosingBefore);

        // Join with org settings for tracked/saved filters
        if (f.TrackedOnly == true || f.SavedOnly == true)
        {
            query = query.Where(t => db.OrgTenderSettings.Any(s =>
                s.TenderId == t.Id && s.OrgId == orgId &&
                (f.TrackedOnly == true ? s.IsTracked : true) &&
                (f.SavedOnly == true ? s.IsSaved : true)));
        }

        var total = await query.CountAsync(ct);
        var page  = Math.Max(1, f.Page);
        var size  = Math.Clamp(f.PageSize, 1, 100);

        var tenderIds = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var settings = await db.OrgTenderSettings
            .Where(s => s.OrgId == orgId && tenderIds.Contains(s.TenderId))
            .ToDictionaryAsync(s => s.TenderId, ct);

        var tenders = await db.Tenders
            .Where(t => tenderIds.Contains(t.Id))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        var items = tenders.Select(t =>
        {
            settings.TryGetValue(t.Id, out var s);
            return new TenderListItemDto(
                t.Id, t.GemTenderId, t.Title,
                t.BuyerOrgName, t.State, t.Category,
                t.TenderValue, t.EmdAmount, t.PublishedDate, t.ClosingDate,
                t.Status, t.AiScore, t.WinProbability,
                s?.IsTracked ?? false, s?.IsSaved ?? false, t.Platform);
        }).ToList();

        return new PagedResult<TenderListItemDto>(items, total, page, size);
    }

    public async Task<TenderDetailDto> GetAsync(Guid tenderId, Guid orgId, CancellationToken ct = default)
    {
        var tender = await db.Tenders
            .Include(t => t.Documents)
            .Include(t => t.AiAnalysis)
            .FirstOrDefaultAsync(t => t.Id == tenderId, ct)
            ?? throw new KeyNotFoundException("Tender not found.");

        var settings = await db.OrgTenderSettings
            .FirstOrDefaultAsync(s => s.TenderId == tenderId && s.OrgId == orgId, ct);

        return MapToDetail(tender, settings);
    }

    public async Task<OrgTenderSettingsDto> SaveAsync(Guid tenderId, Guid orgId, Guid userId, SaveTenderDto dto, CancellationToken ct = default)
    {
        var exists = await db.Tenders.AnyAsync(t => t.Id == tenderId, ct);
        if (!exists) throw new KeyNotFoundException("Tender not found.");

        var settings = await db.OrgTenderSettings
            .FirstOrDefaultAsync(s => s.TenderId == tenderId && s.OrgId == orgId, ct);

        if (settings is null)
        {
            settings = new OrgTenderSettings
            {
                OrgId    = orgId,
                TenderId = tenderId,
                AddedBy  = userId,
                IsTracked = true,
                IsSaved   = true,
            };
            db.OrgTenderSettings.Add(settings);
        }
        else
        {
            settings.IsSaved   = true;
            settings.IsTracked = true;
        }

        if (dto.Notes      is not null) settings.Notes       = dto.Notes;
        if (dto.Tags       is not null) settings.Tags        = dto.Tags;
        if (dto.CustomScore.HasValue)   settings.CustomScore = dto.CustomScore;

        await db.SaveChangesAsync(ct);
        return MapSettings(settings);
    }

    public async Task UnsaveAsync(Guid tenderId, Guid orgId, CancellationToken ct = default)
    {
        var settings = await db.OrgTenderSettings
            .FirstOrDefaultAsync(s => s.TenderId == tenderId && s.OrgId == orgId, ct);

        if (settings is null) return;

        settings.IsSaved  = false;
        settings.IsTracked = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<OrgTenderSettingsDto> UpdateSettingsAsync(Guid tenderId, Guid orgId, SaveTenderDto dto, CancellationToken ct = default)
    {
        var settings = await db.OrgTenderSettings
            .FirstOrDefaultAsync(s => s.TenderId == tenderId && s.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Tender not saved to this organization.");

        if (dto.Notes       is not null) settings.Notes       = dto.Notes;
        if (dto.Tags        is not null) settings.Tags        = dto.Tags;
        if (dto.CustomScore.HasValue)    settings.CustomScore = dto.CustomScore;

        await db.SaveChangesAsync(ct);
        return MapSettings(settings);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static TenderDetailDto MapToDetail(Tender t, OrgTenderSettings? s) => new(
        t.Id, t.GemTenderId, t.Title, t.Description,
        t.BuyerOrgName, t.BuyerOrgIdGem, t.State, t.City,
        t.Category, t.SubCategory, t.TenderValue, t.EmdAmount,
        t.PublishedDate, t.ClosingDate, t.DeliveryDays,
        t.Status, t.CorrigendumCount,
        t.AiScore, t.EligibilityScore, t.WinProbability, t.RiskScore,
        t.AiSummary, t.AiTags, t.CreatedAt, t.UpdatedAt,
        t.Documents.Select(d => new TenderDocumentDto(
            d.Id, d.FileName, d.DocumentType, d.FileSizeKb, d.CreatedAt)).ToList(),
        s is null ? null : MapSettings(s),
        t.AiAnalysis is null ? null : new AiAnalysisResultDto(
            t.AiAnalysis.Id, t.AiAnalysis.ModelUsed,
            t.AiAnalysis.EligibilityBreakdown, t.AiAnalysis.RiskFactors,
            t.AiAnalysis.WinStrategy, t.AiAnalysis.SuggestedBidRange,
            t.AiAnalysis.RequiredDocuments, t.AiAnalysis.KeyClauses,
            t.AiAnalysis.GeneratedAt),
        // Structured procurement detail only exists on the Mongo-sourced path
        // (BiddingBuddyServices). The Postgres entity doesn't carry it.
        Financial:        null,
        Qualification:    null,
        Commercial:       null,
        Compliance:       null,
        Items:            Array.Empty<TenderItemDto>(),
        Ministry:         null,
        Department:       null,
        Office:           null,
        BuyerName:        null,
        BuyerDesignation: null,
        SourceDocuments:  Array.Empty<TenderSourceDocumentDto>(),
        Timeline:         null,
        Platform:         t.Platform);

    private static OrgTenderSettingsDto MapSettings(OrgTenderSettings s)
        => new(s.IsTracked, s.IsSaved, s.CustomScore, s.Notes, s.Tags);
}
