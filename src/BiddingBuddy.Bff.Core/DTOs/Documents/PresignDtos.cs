namespace BiddingBuddy.Bff.Core.DTOs.Documents;

/// <summary>Client request to obtain a presigned PUT URL for direct-to-R2 upload.</summary>
public record RequestUploadUrlDto(
    /// <summary>Original file name (extension preserved, path separators stripped).</summary>
    string FileName,
    /// <summary>MIME type — must be on the server allowlist.</summary>
    string MimeType,
    /// <summary>File size in kilobytes. Must be between 1 and MaxUploadSizeKb (100 MB).</summary>
    int FileSizeKb
);

/// <summary>Response for view and download URL endpoints.</summary>
public record DocumentUrlResponseDto(
    /// <summary>Presigned GET URL — open directly in a browser or pass to a download handler.</summary>
    string Url,
    /// <summary>UTC time when the URL expires (default 15 min).</summary>
    DateTime ExpiresAt
);

/// <summary>Response — gives the client everything it needs to PUT the file directly to R2.</summary>
public record UploadUrlResponseDto(
    /// <summary>Presigned PUT URL. Valid until ExpiresAt.</summary>
    string UploadUrl,
    /// <summary>
    /// R2 object key. Store this and pass it as <c>s3Key</c> when calling
    /// POST /api/documents to register the file after upload.
    /// </summary>
    string ObjectKey,
    /// <summary>Headers the client MUST include in the PUT request (at minimum Content-Type).</summary>
    IReadOnlyDictionary<string, string> Headers,
    /// <summary>UTC time when the presigned URL expires.</summary>
    DateTime ExpiresAt
);
