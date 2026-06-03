namespace BiddingBuddy.Bff.Core.Options;

/// <summary>
/// Cloudflare R2 storage configuration.
/// Non-secret fields are read from appsettings.json.
/// R2:AccessKeyId and R2:SecretAccessKey must be stored in user-secrets (dev)
/// or the deployed secrets store (prod) — never committed.
/// </summary>
public sealed class R2Options
{
    public const string Section = "R2";

    /// <summary>Cloudflare account ID (from R2 dashboard). Used to build the service endpoint.</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>R2 bucket name for org documents.</summary>
    public string BucketName { get; init; } = string.Empty;

    /// <summary>Full S3-compatible endpoint, e.g. https://{AccountId}.r2.cloudflarestorage.com</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>Presigned URL TTL in seconds. Default: 900 (15 min).</summary>
    public int PresignTtlSeconds { get; init; } = 900;

    /// <summary>Maximum allowed upload size in KB. Default: 102400 (100 MB).</summary>
    public int MaxUploadSizeKb { get; init; } = 102_400;

    // ── Secrets — injected from user-secrets / deployed secrets store ─────────

    /// <summary>R2 API token Access Key ID. Set via user-secrets or env var R2__AccessKeyId.</summary>
    public string AccessKeyId { get; init; } = string.Empty;

    /// <summary>R2 API token Secret Access Key. Set via user-secrets or env var R2__SecretAccessKey.</summary>
    public string SecretAccessKey { get; init; } = string.Empty;
}
