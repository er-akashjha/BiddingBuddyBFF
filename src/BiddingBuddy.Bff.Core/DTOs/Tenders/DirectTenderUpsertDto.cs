namespace BiddingBuddy.Bff.Core.DTOs.Tenders;

/// <summary>
/// The canonical closed taxonomy as served by BiddingBuddyServices.
/// Fetched, never duplicated here — see <c>IBiddingBuddyServicesClient.GetTenderTaxonomyAsync</c>.
/// </summary>
public record TenderTaxonomyDto(string[] Categories, string[] States);

/// <summary>
/// The projection of a published buyer-authored tender into the Mongo corpus.
///
/// <para>Mirrors BiddingBuddyServices' <c>TenderUpsertRequest</c> shape, which is what makes a
/// department's notice appear on the public portal, in <c>/explore</c>, in the SEO hub pages and
/// through the supplier matching rail <b>with no new read-side code anywhere</b>. That reuse is the
/// single biggest reason Phase 1 is weeks of work rather than months.</para>
///
/// <para>Declared here rather than shared with Services because the two projects share no assembly
/// — the same reason <c>BiddingBuddy.Contracts</c> is a standing prerequisite in the grants plan.
/// Only the fields a buyer can actually author are modelled: the AI block, scraping metadata and
/// enrichment lifecycle are all server-derived and deliberately absent.</para>
/// </summary>
public record DirectTenderUpsertDto
{
    public DirectTenderSourceDto Source { get; init; } = new();
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public DirectTenderCategoryDto Category { get; init; } = new();
    public DirectTenderOrganizationDto Organization { get; init; } = new();
    public DirectTenderLocationDto Location { get; init; } = new();
    public DirectTenderTimelineDto Timeline { get; init; } = new();
    public DirectTenderFinancialDto Financial { get; init; } = new();
    public DirectTenderCommercialDto Commercial { get; init; } = new();
    public DirectTenderComplianceDto Compliance { get; init; } = new();
    public DirectTenderQualificationDto Qualification { get; init; } = new();
    public DirectTenderItemDto[] Items { get; init; } = [];
    public DirectTenderTechSpecDto[] TechnicalSpecifications { get; init; } = [];
    public DirectTenderDocumentDto[] Documents { get; init; } = [];
    public DirectTenderStatusDto Status { get; init; } = new();
}

public record DirectTenderSourceDto
{
    /// <summary>Always <c>direct</c>. Services overwrites whatever arrives, so a buyer-authored
    /// tender cannot claim to be a GeM one and collide with the scraper's natural key.</summary>
    public string Platform { get; init; } = "direct";

    /// <summary>Our generated reference code (<c>TA-2026-000123</c>) — URL-safe by construction, so
    /// unlike a GeM bid number it never needs escaping in a route.</summary>
    public string PlatformTenderId { get; init; } = string.Empty;

    public string ExternalBidNumber { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public DateTimeOffset? ImportedAt { get; init; }
    public int Version { get; init; } = 1;
}

public record DirectTenderCategoryDto
{
    /// <summary>MUST be canonical. Services rejects anything else on the direct endpoint.</summary>
    public string Primary { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string[] Tags { get; init; } = [];
}

public record DirectTenderOrganizationDto
{
    public string Ministry { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public string Office { get; init; } = string.Empty;
    public string BuyerName { get; init; } = string.Empty;
    public string BuyerDesignation { get; init; } = string.Empty;
    public Dictionary<string, string> BuyerContact { get; init; } = [];
}

public record DirectTenderLocationDto
{
    public string Country { get; init; } = "India";
    /// <summary>MUST be a canonical state/UT name. Services rejects anything else.</summary>
    public string State { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
}

public record DirectTenderTimelineDto
{
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? BidStartAt { get; init; }
    public DateTimeOffset? BidEndAt { get; init; }
    public DateTimeOffset? BidOpeningAt { get; init; }
    public int? ValidityDays { get; init; }
    public string? ContractDuration { get; init; }
}

public record DirectTenderFinancialDto
{
    public decimal? EstimatedBidValue { get; init; }
    public DirectTenderEmdDto Emd { get; init; } = new();
    public DirectTenderEpbgDto Epbg { get; init; } = new();
}

public record DirectTenderEmdDto
{
    public bool Required { get; init; }
    public decimal? Amount { get; init; }
}

public record DirectTenderEpbgDto
{
    public bool Required { get; init; }
    public double? Percentage { get; init; }
}

public record DirectTenderCommercialDto
{
    public string EvaluationMethod { get; init; } = string.Empty;
    public string BidType { get; init; } = string.Empty;
    public DirectTenderReverseAuctionDto ReverseAuction { get; init; } = new();
}

public record DirectTenderReverseAuctionDto
{
    public bool Enabled { get; init; }
}

public record DirectTenderComplianceDto
{
    public bool MiiPreference { get; init; }
    public bool MsePreference { get; init; }
    public double? PurchasePreferencePercent { get; init; }
}

public record DirectTenderQualificationDto
{
    public int? ExperienceYears { get; init; }
    public bool StartupRelaxation { get; init; }
    public bool MseRelaxation { get; init; }
    public string[] RequiredDocuments { get; init; } = [];
    public string[] Certifications { get; init; } = [];
}

public record DirectTenderItemDto
{
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? TotalAmount { get; init; }
    public string Specifications { get; init; } = string.Empty;
}

public record DirectTenderTechSpecDto
{
    public string Group { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}

public record DirectTenderDocumentDto
{
    public string Type { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// The R2 bucket, NOT the AWS tender bucket.
    ///
    /// <para>Buyer uploads are org documents and belong in R2 with everything else the org uploaded;
    /// the AWS <c>bidding-buddy-dev</c> bucket belongs to the scraping pipeline and the two are
    /// never crossed. This matters at read time: the tender presign endpoint resolves its S3 client
    /// by the fixed <c>"TenderS3"</c> key, so a document stored here in R2 cannot be presigned
    /// through that path until the client is selected by bucket. Buyer attachments are therefore
    /// served through the existing org-document presign route instead, which already points at R2.</para>
    /// </summary>
    public string S3Bucket { get; init; } = string.Empty;

    public string S3Key { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public record DirectTenderStatusDto
{
    public string State { get; init; } = "open";
    public bool IsArchived { get; init; }
    public bool IsCancelled { get; init; }
}
