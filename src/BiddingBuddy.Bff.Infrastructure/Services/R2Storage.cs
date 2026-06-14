using Amazon.S3;
using Amazon.S3.Model;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Cloudflare R2 implementation of IR2Storage.
/// Uses the S3-compatible API with ForcePathStyle = true.
/// The IAmazonS3 instance is keyed "R2" in DI — separate from any AWS S3 client
/// used for tender files to avoid credential / bucket confusion.
/// </summary>
public sealed class R2Storage(
    [FromKeyedServices("R2")] IAmazonS3 s3,
    IOptions<R2Options> options,
    ILogger<R2Storage> log) : IR2Storage
{
    private readonly R2Options _cfg = options.Value;

    public Task<PresignedGet> CreatePresignedGetAsync(
        string objectKey,
        string fileName,
        bool   inline,
        CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(_cfg.PresignTtlSeconds);

        // Encode the file name for the Content-Disposition header value
        var encodedName = Uri.EscapeDataString(fileName);
        var disposition  = inline
            ? $"inline; filename=\"{encodedName}\""
            : $"attachment; filename=\"{encodedName}\"";

        var request = new GetPreSignedUrlRequest
        {
            BucketName  = _cfg.BucketName,
            Key         = objectKey,
            Verb        = HttpVerb.GET,
            Expires     = expiresAt,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = disposition,
            },
        };

        var url = s3.GetPreSignedURL(request);

        log.LogDebug(
            "R2 presigned GET ({Mode}) generated for key={Key} expires={Expires}",
            inline ? "inline" : "attachment", objectKey, expiresAt);

        return Task.FromResult(new PresignedGet(url, expiresAt));
    }

    public Task<PresignedUpload> CreatePresignedPutAsync(
        string objectKey,
        string contentType,
        long   sizeBytes,
        CancellationToken ct = default)
    {
        var expiresAt = DateTime.UtcNow.AddSeconds(_cfg.PresignTtlSeconds);

        var request = new GetPreSignedUrlRequest
        {
            BucketName  = _cfg.BucketName,
            Key         = objectKey,
            Verb        = HttpVerb.PUT,
            ContentType = contentType,
            Expires     = expiresAt,
        };

        var url = s3.GetPreSignedURL(request);

        log.LogDebug(
            "R2 presigned PUT generated for key={Key} bucket={Bucket} expires={Expires}",
            objectKey, _cfg.BucketName, expiresAt);

        var result = new PresignedUpload(
            UploadUrl: url,
            ObjectKey: objectKey,
            Headers: new Dictionary<string, string>
            {
                ["Content-Type"] = contentType,
            },
            ExpiresAt: expiresAt);

        return Task.FromResult(result);
    }
}
