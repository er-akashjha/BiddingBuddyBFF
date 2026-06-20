using System.Linq;

namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>
/// Projects the full (authenticated) tender DTOs down to their public/guest
/// equivalents, dropping every org-specific and AI-scoring field. Pure DTO→DTO
/// mapping with no infrastructure dependency, so it lives in Core next to the DTOs.
/// </summary>
public static class PublicTenderMappings
{
    public static PublicTenderListItemDto ToPublic(this TenderListItemDto t) => new(
        Id:            t.Id,
        Title:         t.Title,
        BuyerOrgName:  t.BuyerOrgName,
        State:         t.State,
        Category:      t.Category,
        TenderValue:   t.TenderValue,
        EmdAmount:     t.EmdAmount,
        PublishedDate: t.PublishedDate,
        ClosingDate:   t.ClosingDate,
        Status:        t.Status
    );

    public static PublicPagedTenderListDto ToPublic(this PagedTenderListDto p) => new(
        Items:           p.Items.Select(i => i.ToPublic()).ToList(),
        TotalCount:      p.TotalCount,
        Page:            p.Page,
        PageSize:        p.PageSize,
        TotalPages:      p.TotalPages,
        HasNextPage:     p.HasNextPage,
        HasPreviousPage: p.HasPreviousPage
    );

    public static PublicTenderDetailDto ToPublic(this TenderDetailDto t) => new(
        Id:               t.Id,
        Title:            t.Title,
        Description:      t.Description,
        BuyerOrgName:     t.BuyerOrgName,
        State:            t.State,
        City:             t.City,
        Category:         t.Category,
        SubCategory:      t.SubCategory,
        TenderValue:      t.TenderValue,
        EmdAmount:        t.EmdAmount,
        PublishedDate:    t.PublishedDate,
        ClosingDate:      t.ClosingDate,
        DeliveryDays:     t.DeliveryDays,
        Status:           t.Status,
        Tags:             t.AiTags,
        CreatedAt:        t.CreatedAt,
        UpdatedAt:        t.UpdatedAt,
        Financial:        t.Financial,
        Qualification:    t.Qualification,
        Commercial:       t.Commercial,
        Compliance:       t.Compliance,
        Items:            t.Items,
        Ministry:         t.Ministry,
        Department:       t.Department,
        Office:           t.Office,
        BuyerName:        t.BuyerName,
        BuyerDesignation: t.BuyerDesignation,
        SourceDocuments:  t.SourceDocuments,
        Timeline:         t.Timeline
    );
}
