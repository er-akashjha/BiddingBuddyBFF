namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>
/// Read-only access to the scraped-tender-files S3 bucket. The BFF only generates
/// short-lived presigned GET URLs — it never streams the bytes or writes objects.
/// Separate from <see cref="IR2Storage"/> (org documents) by design.
/// </summary>
public interface ITenderFileStorage
{
    /// <summary>Default bucket name (from config) used when a document has no stored bucket.</summary>
    string DefaultBucket { get; }

    /// <summary>
    /// Reconstruct the S3 key for a document of a tender that was enriched before
    /// s3Key was persisted: <c>gem/{platformTenderId with '/' → '-'}/{documentId}.pdf</c>.
    /// </summary>
    string ReconstructKey(string platformTenderId, string documentId);

    /// <summary>Generate a presigned GET URL for a tender file (attachment download by default).</summary>
    Task<PresignedGet> CreatePresignedGetAsync(
        string bucket,
        string objectKey,
        string fileName,
        bool   inline = false,
        CancellationToken ct = default);
}
