using Amazon.S3;
using Amazon.S3.Model;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// AWS S3 implementation of <see cref="ITenderFileStorage"/> for the scraped-tender
/// bucket (e.g. <c>bidding-buddy-dev</c>). Uses the IAmazonS3 instance keyed "TenderS3"
/// in DI — separate from the "R2" client to keep credentials and buckets isolated.
/// </summary>
public sealed class TenderFileStorage(
    [FromKeyedServices("TenderS3")] IAmazonS3 s3,
    IOptions<TenderS3Options> options,
    ILogger<TenderFileStorage> log) : ITenderFileStorage
{
    private readonly TenderS3Options _cfg = options.Value;

    public string DefaultBucket => _cfg.BucketName;

    public string ReconstructKey(string platformTenderId, string documentId)
    {
        // Mirrors BiddingBuddyDownloader: Constants.S3FilePrefix = "gem/{0}/{1}",
        // platformTenderId has '/' replaced with '-', file is {documentId}{ext}.
        // Extension is unknown for legacy records, so we assume .pdf (the common case).
        var safeTenderId = (platformTenderId ?? string.Empty).Replace('/', '-');
        return $"{_cfg.KeyPrefix}/{safeTenderId}/{documentId}.pdf";
    }

    public Task<PresignedGet> CreatePresignedGetAsync(
        string bucket,
        string objectKey,
        string fileName,
        bool   inline = false,
        CancellationToken ct = default)
    {
        var expiresAt   = DateTime.UtcNow.AddSeconds(_cfg.PresignTtlSeconds);
        var encodedName = Uri.EscapeDataString(fileName);
        var disposition = inline
            ? $"inline; filename=\"{encodedName}\""
            : $"attachment; filename=\"{encodedName}\"";

        var request = new GetPreSignedUrlRequest
        {
            BucketName = string.IsNullOrWhiteSpace(bucket) ? _cfg.BucketName : bucket,
            Key        = objectKey,
            Verb       = HttpVerb.GET,
            Expires    = expiresAt,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = disposition,
            },
        };

        var url = s3.GetPreSignedURL(request);

        log.LogDebug(
            "Tender S3 presigned GET ({Mode}) generated for bucket={Bucket} key={Key} expires={Expires}",
            inline ? "inline" : "attachment", request.BucketName, objectKey, expiresAt);

        return Task.FromResult(new PresignedGet(url, expiresAt));
    }
}
