using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Pay-gated, on-demand AI enrichment. See <see cref="IEnrichmentService"/>.
/// The two idempotency guards are split across two stores on purpose:
///   • Postgres UNIQUE(org_id, gem_tender_id) — anti-double-CHARGE (per org).
///   • Mongo conditional status claim          — anti-double-ENRICH (per tender, global).
/// We publish the AI run ONLY when a new entitlement was granted AND the Mongo claim was
/// won, so N orgs paying for the same un-enriched tender cause exactly one AI run.
/// </summary>
public class EnrichmentService(
    BffDbContext db,
    IBiddingBuddyServicesClient servicesClient,
    IRabbitMqPublisher publisher,
    ILogger<EnrichmentService> log) : IEnrichmentService
{
    private const string EnrichRequestedQueue = "bid.enrich-requested";

    public async Task<bool> IsUnlockedAsync(Guid orgId, string gemTenderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(gemTenderId)) return false;
        return await db.TenderEnrichmentEntitlements
            .AnyAsync(e => e.OrgId == orgId && e.GemTenderId == gemTenderId && e.Status == "unlocked", ct);
    }

    public async Task<HashSet<string>> GetUnlockedSetAsync(
        Guid orgId, IReadOnlyCollection<string> gemTenderIds, CancellationToken ct = default)
    {
        var ids = gemTenderIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        if (ids.Count == 0) return new HashSet<string>();

        var unlocked = await db.TenderEnrichmentEntitlements
            .Where(e => e.OrgId == orgId && e.Status == "unlocked" && ids.Contains(e.GemTenderId))
            .Select(e => e.GemTenderId)
            .ToListAsync(ct);

        return new HashSet<string>(unlocked);
    }

    public async Task<EnrichmentStatusDto> GetStatusAsync(Guid orgId, Guid tenderId, CancellationToken ct = default)
    {
        var raw         = await servicesClient.GetRawTenderAsync(tenderId.ToString(), ct);
        var gemTenderId = raw?.Source?.PlatformTenderId ?? string.Empty;
        var global      = (raw?.EnrichmentStatus ?? "none").ToLowerInvariant();

        var ent = string.IsNullOrWhiteSpace(gemTenderId)
            ? null
            : await db.TenderEnrichmentEntitlements
                .FirstOrDefaultAsync(e => e.OrgId == orgId && e.GemTenderId == gemTenderId, ct);

        return Resolve(ent?.Status, global, ent?.UnlockedAt);
    }

    public async Task<EnrichmentStatusDto> RequestEnrichmentAsync(
        Guid orgId, Guid userId, Guid tenderId, CancellationToken ct = default)
    {
        var raw = await servicesClient.GetRawTenderAsync(tenderId.ToString(), ct)
            ?? throw new KeyNotFoundException($"Tender {tenderId} not found.");

        var gemTenderId = raw.Source?.PlatformTenderId;
        if (string.IsNullOrWhiteSpace(gemTenderId))
            throw new InvalidOperationException("Tender has no platform tender id; cannot enrich.");

        var global = (raw.EnrichmentStatus ?? "none").ToLowerInvariant();

        // 1. Idempotent per-org grant (anti-double-charge). rows==1 ⇒ newly granted.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO tender_enrichment_entitlements (org_id, gem_tender_id, status, source, unlocked_by_user_id, created_at)
            VALUES ({orgId}, {gemTenderId}, 'pending', 'grant', {userId}, now())
            ON CONFLICT (org_id, gem_tender_id) DO NOTHING", ct);

        // 2. Already enriched globally → unlock this org immediately (reuse global data, zero AI cost).
        if (global == "enriched")
        {
            await db.TenderEnrichmentEntitlements
                .Where(e => e.OrgId == orgId && e.GemTenderId == gemTenderId && e.Status != "unlocked")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Status, "unlocked")
                    .SetProperty(e => e.UnlockedAt, DateTime.UtcNow), ct);
            return Resolve("unlocked", "enriched", DateTime.UtcNow);
        }

        // 3. Org already had an entitlement (insert was a no-op) → the run is already in
        //    flight for this org; don't re-trigger.
        if (inserted == 0)
            return await GetStatusAsync(orgId, tenderId, ct);

        // 4. New grant on an un-enriched tender → atomically claim the Mongo state machine.
        //    Only the request that wins the claim publishes, so concurrent payers cause one run.
        bool claimed;
        try
        {
            claimed = await servicesClient.SetEnrichmentStatusAsync(
                "gem", gemTenderId, "Queued",
                allowedCurrent: new[] { "None", "Extracted", "Failed" },
                paidByOrg: orgId.ToString(), ct: ct);
        }
        catch (Exception ex)
        {
            // Entitlement is granted; the run just couldn't be enqueued. The org sees
            // "queued" and a later retry / scan can pick it up.
            log.LogError(ex, "Enrichment claim failed for {Gem}; entitlement granted but not enqueued", gemTenderId);
            return Resolve("pending", global, null);
        }

        if (claimed)
        {
            await PublishEnrichRequestAsync(raw, gemTenderId!, orgId, userId, ct);
            return Resolve("pending", "queued", null);
        }

        // Claim lost — another request already queued/processing this tender globally.
        return Resolve("pending", "processing", null);
    }

    private async Task PublishEnrichRequestAsync(
        TenderSearchItemDto raw, string gemTenderId, Guid orgId, Guid userId, CancellationToken ct)
    {
        var docs = raw.Documents ?? new List<TenderDocumentRefDto>();

        // The bid PDF is the document whose extracted content drives enrichment.
        var bidDoc = docs.FirstOrDefault(d =>
                         string.Equals(d.Type, "bid", StringComparison.OrdinalIgnoreCase) &&
                         !string.IsNullOrWhiteSpace(d.S3Key))
                  ?? docs.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.S3Key));

        if (bidDoc is null || string.IsNullOrWhiteSpace(bidDoc.DocumentId))
        {
            log.LogWarning(
                "No stored bid document for {Gem}; cannot publish enrich request (tender granted but not enqueued)",
                gemTenderId);
            return;
        }

        // Display-only buyer attachments (carried as direct platform links).
        var attachments = docs
            .Where(d => !string.IsNullOrWhiteSpace(d.Url))
            .Select(d => new
            {
                documentId = d.DocumentId ?? string.Empty,
                fileName   = d.FileName ?? string.Empty,
                url        = d.Url ?? string.Empty,
                type       = string.IsNullOrWhiteSpace(d.Type) ? "attachment" : d.Type,
            })
            .ToArray();

        var correlationId = Guid.NewGuid();

        // Shape MUST match BidProcessor's EnrichRequestedEvent (camelCase JSON).
        var payload = new
        {
            correlationId = correlationId.ToString(),
            tenderId    = raw.Id,
            bidNumber   = gemTenderId,
            documentId  = bidDoc.DocumentId,
            s3Bucket    = bidDoc.S3Bucket ?? string.Empty,
            s3Key       = bidDoc.S3Key ?? string.Empty,
            fileName    = bidDoc.FileName ?? string.Empty,
            attachments,
            orgId       = orgId.ToString(),
            requestedBy = userId.ToString(),
        };

        var ok = await publisher.PublishAsync(EnrichRequestedQueue, payload, correlationId, ct);
        if (!ok)
            log.LogError("Failed to publish enrich request for {Gem} to {Queue}", gemTenderId, EnrichRequestedQueue);
    }

    /// <summary>Maps (entitlement status, global tender status) → the resolved UI status.</summary>
    private static EnrichmentStatusDto Resolve(string? entitlementStatus, string globalStatus, DateTime? unlockedAt)
    {
        if (entitlementStatus is null)
            return new EnrichmentStatusDto("locked", false, globalStatus, null);
        if (entitlementStatus == "unlocked")
            return new EnrichmentStatusDto("enriched", true, globalStatus, unlockedAt);
        if (entitlementStatus == "failed")
            return new EnrichmentStatusDto("failed", true, globalStatus, null);

        // pending: reflect the global run state (never "enriched" — AI isn't unlocked yet).
        var s = globalStatus switch
        {
            "failed"                  => "failed",
            "processing" or "enriched" => "processing",
            _                          => "queued",
        };
        return new EnrichmentStatusDto(s, true, globalStatus, null);
    }
}
