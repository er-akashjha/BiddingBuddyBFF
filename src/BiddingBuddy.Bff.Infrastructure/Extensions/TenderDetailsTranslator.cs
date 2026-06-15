using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BiddingBuddy.Bff.Infrastructure.Extensions
{
    public static class TenderDetailsTranslator
    {
        public static TenderDetailDto ToDetailsDto(this TenderSearchItemDto tender)
        {
            // Safely convert nullable DateTime? → DateOnly? without throwing on null
            static DateOnly? ToDateOnly(DateTime? dt) =>
                dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

            return new TenderDetailDto(
                Id:               Guid.Parse(tender.Id),
                GemTenderId:      tender.Source?.PlatformTenderId ?? string.Empty,
                Title:            tender.Title ?? string.Empty,
                Description:      tender.Summary,
                BuyerOrgName:     tender.Organization?.Organization,
                BuyerOrgIdGem:    tender.Id,
                State:            tender.Location?.State,
                City:             tender.Location?.City,
                Category:         tender.Category?.Primary,
                SubCategory:      tender.Category?.Secondary,
                TenderValue:      tender.Financial?.EstimatedBidValue,
                EmdAmount:        tender.Financial?.Emd?.Amount,
                PublishedDate:    ToDateOnly(tender.Timeline?.PublishedAt),
                ClosingDate:      ToDateOnly(tender.Timeline?.BidEndAt),
                DeliveryDays:     tender.Timeline?.ValidityDays,
                Status:           tender.Status?.State ?? "unknown",
                CorrigendumCount: 0,
                AiScore:          tender.Ai?.OpportunityScore ?? 0,
                EligibilityScore: 0,
                WinProbability:   0,
                RiskScore:        tender.Ai?.RiskScore ?? 0,
                AiSummary:        tender.Ai?.Summary,
                AiTags:           tender.Category?.Tags ?? Array.Empty<string>(),
                CreatedAt:        tender.CreatedAt,
                UpdatedAt:        tender.UpdatedAt,
                Documents:        Array.Empty<TenderDocumentDto>(),
                OrgSettings:      null,
                AiAnalysis:       null,
                // Pass the rich procurement detail straight through to the client
                Financial:        tender.Financial,
                Qualification:    tender.Qualification,
                Commercial:       tender.Commercial,
                Compliance:       tender.Compliance,
                Items:            tender.Items ?? Array.Empty<TenderItemDto>(),
                Ministry:         tender.Organization?.Ministry,
                Department:       tender.Organization?.Department,
                Office:           tender.Organization?.Office,
                BuyerName:        tender.Organization?.BuyerName,
                BuyerDesignation: tender.Organization?.BuyerDesignation,
                SourceDocuments:  (tender.Documents ?? new List<TenderDocumentRefDto>())
                    .Where(d => !string.IsNullOrWhiteSpace(d.DocumentId))
                    .Select(d => new TenderSourceDocumentDto(
                        DocumentId:   d.DocumentId!,
                        Type:         d.Type,
                        FileName:     d.FileName,
                        HasStoredKey: !string.IsNullOrWhiteSpace(d.S3Key)))
                    .ToList(),
                Timeline:         tender.Timeline);
        }


        public static List<TenderListItemDto> ToListDto(this IReadOnlyList<TenderSearchItemDto> items)
        {
            // Safely convert nullable DateTime? → DateOnly? without throwing on null
            static DateOnly? ToDateOnly(DateTime? dt) =>
                dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

            var tenderList = new List<TenderListItemDto>();

            foreach (var item in items)
            {
                var gemId = item.Source?.PlatformTenderId ?? string.Empty;

                var dto = new TenderListItemDto(
                    Id:            Guid.Parse(item.Id),
                    GemTenderId:   gemId,
                    // Fall back to AI-normalised title, then GEM ID when title is blank
                    Title:         !string.IsNullOrWhiteSpace(item.Title)
                                       ? item.Title
                                       : (!string.IsNullOrWhiteSpace(item.Ai?.NormalizedTitle)
                                           ? item.Ai!.NormalizedTitle
                                           : gemId),
                    BuyerOrgName:  item.Organization?.Organization,
                    State:         item.Location?.State,
                    Category:      item.Category?.Primary,
                    TenderValue:   item.Financial?.EstimatedBidValue,
                    EmdAmount:     item.Financial?.Emd?.Amount,
                    PublishedDate: ToDateOnly(item.Timeline?.PublishedAt),
                    ClosingDate:   ToDateOnly(item.Timeline?.BidEndAt),
                    Status:        item.Status?.State ?? "unknown",
                    AiScore:       item.Ai?.OpportunityScore ?? 0,
                    WinProbability: 0,   // not available in TenderSearchItemDto
                    IsTracked:     false, // not available at this stage
                    IsSaved:       false  // not available at this stage
                );

                tenderList.Add(dto); // include all tenders regardless of title
            }

            return tenderList;
        }
    }
}
