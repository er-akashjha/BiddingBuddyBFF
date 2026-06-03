using BiddingBuddy.Bff.Core.DTOs.Internal;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class InternalPipelineService(BffDbContext db) : IInternalPipelineService
{
    public async Task<UpsertTenderResponseDto> UpsertTenderAsync(UpsertTenderDto dto, CancellationToken ct = default)
    {
        var existing = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == dto.GemTenderId, ct);

        if (existing is null)
        {
            var tender = new Tender
            {
                GemTenderId      = dto.GemTenderId,
                Title            = dto.Title,
                Description      = dto.Description,
                BuyerOrgName     = dto.BuyerOrgName,
                BuyerOrgIdGem    = dto.BuyerOrgIdGem,
                State            = dto.State,
                City             = dto.City,
                Category         = dto.Category,
                SubCategory      = dto.SubCategory,
                TenderValue      = dto.TenderValue,
                EmdAmount        = dto.EmdAmount,
                PublishedDate    = dto.PublishedDate,
                ClosingDate      = dto.ClosingDate,
                DeliveryDays     = dto.DeliveryDays,
                Status           = dto.Status ?? "active",
                CorrigendumCount = dto.CorrigendumCount ?? 0,
                AiScore          = dto.AiScore,
                EligibilityScore = dto.EligibilityScore,
                WinProbability   = dto.WinProbability,
                RiskScore        = dto.RiskScore,
                AiSummary        = dto.AiSummary,
                AiTags           = dto.AiTags,
                RawData          = dto.RawData,
            };
            db.Tenders.Add(tender);
            await db.SaveChangesAsync(ct);
            return new UpsertTenderResponseDto(tender.Id, true);
        }

        existing.Title            = dto.Title;
        existing.Description      = dto.Description ?? existing.Description;
        existing.BuyerOrgName     = dto.BuyerOrgName ?? existing.BuyerOrgName;
        existing.BuyerOrgIdGem    = dto.BuyerOrgIdGem ?? existing.BuyerOrgIdGem;
        existing.State            = dto.State ?? existing.State;
        existing.City             = dto.City ?? existing.City;
        existing.Category         = dto.Category ?? existing.Category;
        existing.SubCategory      = dto.SubCategory ?? existing.SubCategory;
        existing.TenderValue      = dto.TenderValue ?? existing.TenderValue;
        existing.EmdAmount        = dto.EmdAmount ?? existing.EmdAmount;
        existing.PublishedDate    = dto.PublishedDate ?? existing.PublishedDate;
        existing.ClosingDate      = dto.ClosingDate ?? existing.ClosingDate;
        existing.DeliveryDays     = dto.DeliveryDays ?? existing.DeliveryDays;
        existing.Status           = dto.Status ?? existing.Status;
        existing.CorrigendumCount = dto.CorrigendumCount ?? existing.CorrigendumCount;
        existing.AiScore          = dto.AiScore ?? existing.AiScore;
        existing.EligibilityScore = dto.EligibilityScore ?? existing.EligibilityScore;
        existing.WinProbability   = dto.WinProbability ?? existing.WinProbability;
        existing.RiskScore        = dto.RiskScore ?? existing.RiskScore;
        existing.AiSummary        = dto.AiSummary ?? existing.AiSummary;
        existing.AiTags           = dto.AiTags ?? existing.AiTags;
        if (dto.RawData is not null) existing.RawData = dto.RawData;

        await db.SaveChangesAsync(ct);
        return new UpsertTenderResponseDto(existing.Id, false);
    }

    public async Task UpsertDocumentContentAsync(string gemTenderId, UpsertDocumentContentDto dto, CancellationToken ct = default)
    {
        var tender = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == gemTenderId, ct)
            ?? throw new KeyNotFoundException($"Tender with GEM ID '{gemTenderId}' not found.");

        var existing = await db.TenderDocuments
            .FirstOrDefaultAsync(d => d.TenderId == tender.Id && d.S3Key == dto.S3Key, ct);

        if (existing is null)
        {
            db.TenderDocuments.Add(new TenderDocument
            {
                TenderId      = tender.Id,
                FileName      = dto.FileName,
                S3Key         = dto.S3Key,
                DocumentType  = dto.DocumentType,
                FileSizeKb    = dto.FileSizeKb,
                ExtractedText = dto.ExtractedText,
            });
        }
        else
        {
            existing.FileName      = dto.FileName;
            existing.DocumentType  = dto.DocumentType ?? existing.DocumentType;
            existing.FileSizeKb    = dto.FileSizeKb ?? existing.FileSizeKb;
            existing.ExtractedText = dto.ExtractedText ?? existing.ExtractedText;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertCompetitorAsync(UpsertCompetitorDto dto, CancellationToken ct = default)
    {
        var existing = await db.Competitors
            .FirstOrDefaultAsync(c => c.OrgId == dto.OrgId &&
                (c.GemSellerId == dto.GemSellerId || c.CompanyName == dto.CompanyName), ct);

        if (existing is null)
        {
            db.Competitors.Add(new Competitor
            {
                OrgId            = dto.OrgId,
                CompanyName      = dto.CompanyName,
                GemSellerId      = dto.GemSellerId,
                Tier             = dto.Tier,
                ThreatLevel      = dto.ThreatLevel,
                WinRate          = dto.WinRate,
                TotalContracts   = dto.TotalContracts ?? 0,
                TotalWinValue    = dto.TotalWinValue,
                AvgBidValue      = dto.AvgBidValue,
                ActiveStates     = dto.ActiveStates,
                ActiveCategories = dto.ActiveCategories,
                FirstSeenAt      = dto.FirstSeenAt,
                LastSeenAt       = dto.LastSeenAt,
            });
        }
        else
        {
            if (dto.GemSellerId     is not null) existing.GemSellerId      = dto.GemSellerId;
            if (dto.Tier            is not null) existing.Tier             = dto.Tier;
            if (dto.ThreatLevel     is not null) existing.ThreatLevel      = dto.ThreatLevel;
            if (dto.WinRate.HasValue)            existing.WinRate          = dto.WinRate;
            if (dto.TotalContracts.HasValue)     existing.TotalContracts   = dto.TotalContracts.Value;
            if (dto.TotalWinValue.HasValue)      existing.TotalWinValue    = dto.TotalWinValue;
            if (dto.AvgBidValue.HasValue)        existing.AvgBidValue      = dto.AvgBidValue;
            if (dto.ActiveStates     is not null) existing.ActiveStates    = dto.ActiveStates;
            if (dto.ActiveCategories is not null) existing.ActiveCategories = dto.ActiveCategories;
            if (dto.LastSeenAt.HasValue)         existing.LastSeenAt       = dto.LastSeenAt;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpsertAiAnalysisAsync(UpsertAiAnalysisDto dto, CancellationToken ct = default)
    {
        var tender = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == dto.GemTenderId, ct)
            ?? throw new KeyNotFoundException($"Tender with GEM ID '{dto.GemTenderId}' not found.");

        var existing = await db.AiAnalysisResults
            .FirstOrDefaultAsync(a => a.TenderId == tender.Id, ct);

        if (existing is null)
        {
            db.AiAnalysisResults.Add(new AiAnalysisResult
            {
                TenderId              = tender.Id,
                ModelUsed             = dto.ModelUsed,
                EligibilityBreakdown  = dto.EligibilityBreakdown,
                RiskFactors           = dto.RiskFactors,
                WinStrategy           = dto.WinStrategy,
                SuggestedBidRange     = dto.SuggestedBidRange,
                RequiredDocuments     = dto.RequiredDocuments,
                KeyClauses            = dto.KeyClauses,
                RawResponse           = dto.RawResponse,
                GeneratedAt           = DateTime.UtcNow,
            });
        }
        else
        {
            if (dto.ModelUsed            is not null) existing.ModelUsed            = dto.ModelUsed;
            if (dto.EligibilityBreakdown is not null) existing.EligibilityBreakdown = dto.EligibilityBreakdown;
            if (dto.RiskFactors          is not null) existing.RiskFactors          = dto.RiskFactors;
            if (dto.WinStrategy          is not null) existing.WinStrategy          = dto.WinStrategy;
            if (dto.SuggestedBidRange    is not null) existing.SuggestedBidRange    = dto.SuggestedBidRange;
            if (dto.RequiredDocuments    is not null) existing.RequiredDocuments    = dto.RequiredDocuments;
            if (dto.KeyClauses           is not null) existing.KeyClauses           = dto.KeyClauses;
            if (dto.RawResponse          is not null) existing.RawResponse          = dto.RawResponse;
            existing.GeneratedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
