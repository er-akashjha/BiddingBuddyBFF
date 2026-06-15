namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// AWS S3 configuration for the scraped-tender-files bucket (e.g. <c>bidding-buddy-dev</c>),
/// owned by the Downloader / BidProcessor pipeline.
///
/// This is intentionally separate from <see cref="R2Options"/>: R2 holds org-uploaded
/// documents, AWS S3 holds the tender PDFs scraped from GeM. The BFF only ever presigns
/// short-lived GET URLs against this bucket — it never writes to it.
///
/// AccessKeyId / SecretAccessKey must come from user-secrets (dev) or the deployed
/// secrets store (prod) — never committed.
/// </summary>
public sealed class TenderS3Options
{
    public const string Section = "TenderS3";

    /// <summary>AWS region, e.g. <c>ap-south-1</c>.</summary>
    public string Region { get; init; } = "ap-south-1";

    /// <summary>Bucket holding scraped tender files. Default: <c>bidding-buddy-dev</c>.</summary>
    public string BucketName { get; init; } = "bidding-buddy-dev";

    /// <summary>S3 key prefix template for reconstructing keys of tenders enriched before
    /// s3Key was persisted: <c>gem/{platformTenderId}/{documentId}{ext}</c>.</summary>
    public string KeyPrefix { get; init; } = "gem";

    /// <summary>Presigned URL TTL in seconds. Default: 900 (15 min).</summary>
    public int PresignTtlSeconds { get; init; } = 900;

    // ── Secrets — injected from user-secrets / deployed secrets store ─────────

    /// <summary>AWS Access Key ID. Set via user-secrets or env var TenderS3__AccessKeyId.</summary>
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>AWS Secret Access Key. Set via user-secrets or env var TenderS3__SecretAccessKey.</summary>
    public string SecretAccessKey { get; init; } = string.Empty;
}
