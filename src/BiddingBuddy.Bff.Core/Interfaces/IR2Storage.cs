namespace BiddingBuddy.Bff.Core.Interfaces;

/// <summary>Cloudflare R2 storage abstraction (S3-compatible).</summary>
public interface IR2Storage
{
    /// <summary>
    /// Generate a presigned PUT URL the client can use to upload a file directly
    /// to R2 without routing binary data through the BFF.
    /// </summary>
    Task<PresignedUpload> CreatePresignedPutAsync(
        string objectKey,
        string contentType,
        long   sizeBytes,
        CancellationToken ct = default);
}

/// <summary>Result returned to the client so it can PUT the file directly to R2.</summary>
public record PresignedUpload(
    /// <summary>The presigned PUT URL. Valid for PresignTtlSeconds.</summary>
    string UploadUrl,
    /// <summary>Server-side object key — store this and pass it to POST /api/documents after upload.</summary>
    string ObjectKey,
    /// <summary>Headers the client MUST include in the PUT request.</summary>
    IReadOnlyDictionary<string, string> Headers,
    /// <summary>UTC timestamp when the URL expires.</summary>
    DateTime ExpiresAt
);
