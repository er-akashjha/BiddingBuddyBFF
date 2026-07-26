using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// The deadline / expiry reminder engine. Each sub-scan pulls the rows in its actionable
/// window (AsNoTracking — see <see cref="NotificationAudienceResolver"/>), and for each one
/// atomically claims a (entity, milestone) row in <c>notification_reminders</c> before
/// dispatching. The claim (INSERT … ON CONFLICT DO NOTHING) is what makes every reminder
/// fire exactly once across ticks / instances.
///
/// Every payload carries OrgId + EntityId so the InApp sender can write a complete
/// user_notifications row (org-scoped + deep-linkable). Routing follows the plan: bids/tasks →
/// assignee (or bid managers if unassigned); invoice/EMD → finance + admins; compliance →
/// bid managers + admins; delivery → sales + admins.
/// </summary>
public class DeadlineScanService(
    BffDbContext db,
    INotificationPublisher publisher,
    INotificationAudienceResolver audience,
    IConfiguration config,
    IOptions<DeadlineScanOptions> options,
    ILogger<DeadlineScanService> log) : IDeadlineScanService
{
    // One scan at a time per process (worker vs. any manual trigger vs. itself).
    private static readonly SemaphoreSlim ScanGate = new(1, 1);

    private static readonly string[] BidRoles        = ["owner", "admin", "bid_manager"];
    private static readonly string[] FinanceRoles    = ["owner", "admin", "finance"];
    private static readonly string[] ComplianceRoles = ["owner", "admin", "bid_manager"];
    private static readonly string[] DeliveryRoles   = ["owner", "admin", "sales"];

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<DeadlineScanResult> RunAsync(CancellationToken ct = default)
    {
        if (!await ScanGate.WaitAsync(0, ct))
        {
            log.LogInformation("[DeadlineScan] Skipped — another scan is already running.");
            return new DeadlineScanResult(0, Skipped: true);
        }

        try
        {
            var opt   = options.Value;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var sent  = 0;

            sent += await ScanBidsAsync(today, opt, ct);
            sent += await ScanChecklistAsync(today, opt, ct);
            sent += await ScanInvoicesAsync(today, opt, ct);
            sent += await ScanComplianceAsync(today, opt, ct);
            sent += await ScanDeliveryMilestonesAsync(today, ct);
            sent += await ScanEmdAsync(today, opt, ct);
            sent += await ScanEmdDispatchDueAsync(today, opt, ct);
            sent += await ScanEmdDeliveryOverdueAsync(today, ct);
            sent += await ScanEmdInstrumentExpiryAsync(today, opt, ct);

            if (sent > 0)
                log.LogInformation("[DeadlineScan] Run complete — {Sent} reminder(s) dispatched.", sent);
            return new DeadlineScanResult(sent);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    // ── Bids: submission due soon / overdue ─────────────────────────────────────
    private async Task<int> ScanBidsAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.BidDueLeadDays);
        var bids = await db.Bids
            .AsNoTracking()
            .Where(b => b.StatusCategory == "open" && b.DueDate != null && b.DueDate <= horizon)
            .Select(b => new { b.Id, b.OrgId, b.Title, b.DueDate, b.AssignedTo })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var b in bids)
        {
            var days = b.DueDate!.Value.DayNumber - today.DayNumber;
            var (key, template) = days < 0 ? ("BID_OVERDUE", "BID_OVERDUE") : ("BID_DUE_SOON", "BID_DUE_SOON");

            var recipients = b.AssignedTo is Guid a
                ? await OneAsync(a, ct)
                : await audience.ByRolesAsync(b.OrgId, BidRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(b.OrgId, "bid", b.Id, key, ct)) continue;

            sent += await DispatchAsync(template, NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"] = FirstNameOf(m.Name),
                ["BidTitle"]  = b.Title,
                ["DueText"]   = RelativeDayText(days),
                ["DueDate"]   = b.DueDate!.Value.ToString("dd MMM yyyy"),
                ["OrgId"]     = b.OrgId.ToString(),
                ["EntityId"]  = b.Id.ToString(),
                ["Link"]      = $"{FrontendBaseUrl}/bids/{b.Id}",
            }, ct);
        }
        return sent;
    }

    // ── Bid checklist tasks: due soon / overdue ─────────────────────────────────
    private async Task<int> ScanChecklistAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.BidDueLeadDays);
        var items = await db.BidChecklistItems
            .AsNoTracking()
            .Where(i => !i.IsDone && i.DueDate != null && i.DueDate <= horizon)
            .Join(db.Bids, i => i.BidId, b => b.Id,
                (i, b) => new { i.Id, i.OrgId, i.BidId, TaskTitle = i.Title, i.DueDate, i.AssignedTo, BidTitle = b.Title })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var i in items)
        {
            var days = i.DueDate!.Value.DayNumber - today.DayNumber;
            var (key, template) = days < 0 ? ("BID_TASK_OVERDUE", "BID_TASK_OVERDUE") : ("BID_TASK_DUE_SOON", "BID_TASK_DUE_SOON");

            var recipients = i.AssignedTo is Guid a
                ? await OneAsync(a, ct)
                : await audience.ByRolesAsync(i.OrgId, BidRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(i.OrgId, "bid_checklist", i.Id, key, ct)) continue;

            sent += await DispatchAsync(template, NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"] = FirstNameOf(m.Name),
                ["TaskTitle"] = i.TaskTitle,
                ["BidTitle"]  = i.BidTitle,
                ["DueText"]   = RelativeDayText(days),
                ["DueDate"]   = i.DueDate!.Value.ToString("dd MMM yyyy"),
                ["OrgId"]     = i.OrgId.ToString(),
                ["EntityId"]  = i.BidId.ToString(),
                ["Link"]      = $"{FrontendBaseUrl}/bids/{i.BidId}",
            }, ct);
        }
        return sent;
    }

    // ── Invoices: payment due soon / overdue ────────────────────────────────────
    private async Task<int> ScanInvoicesAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.InvoiceDueLeadDays);
        var invoices = await db.Invoices
            .AsNoTracking()
            .Where(i => i.Status != "paid" && i.DueDate != null && i.DueDate <= horizon)
            .Select(i => new { i.Id, i.OrgId, i.InvoiceNumber, i.BuyerOrg, i.Amount, i.TotalAmount, i.DueDate })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var i in invoices)
        {
            var days = i.DueDate!.Value.DayNumber - today.DayNumber;
            var (key, template) = days < 0 ? ("INVOICE_OVERDUE", "INVOICE_OVERDUE") : ("INVOICE_DUE_SOON", "INVOICE_DUE_SOON");

            var recipients = await audience.ByRolesAsync(i.OrgId, FinanceRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(i.OrgId, "invoice", i.Id, key, ct)) continue;

            sent += await DispatchAsync(template, NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]     = FirstNameOf(m.Name),
                ["InvoiceNumber"] = i.InvoiceNumber ?? "—",
                ["BuyerOrg"]      = i.BuyerOrg ?? "",
                ["Amount"]        = (i.TotalAmount ?? i.Amount).ToString("N0"),
                ["DueText"]       = RelativeDayText(days),
                ["DueDate"]       = i.DueDate!.Value.ToString("dd MMM yyyy"),
                ["OrgId"]         = i.OrgId.ToString(),
                ["EntityId"]      = i.Id.ToString(),
                ["Link"]          = $"{FrontendBaseUrl}/payments",
            }, ct);
        }
        return sent;
    }

    // ── Compliance documents: certificate expiring / expired ────────────────────
    private async Task<int> ScanComplianceAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.ComplianceExpiryLeadDays);
        var docs = await db.ComplianceDocuments
            .AsNoTracking()
            .Where(d => d.ExpiryDate != null && d.ExpiryDate <= horizon)
            .Join(db.ComplianceRequirements, d => d.RequirementId, r => r.Id,
                (d, r) => new { d.Id, d.OrgId, d.ExpiryDate, DocName = r.Name })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var d in docs)
        {
            var days = d.ExpiryDate!.Value.DayNumber - today.DayNumber;
            var (key, template) = days < 0 ? ("COMPLIANCE_EXPIRED", "COMPLIANCE_EXPIRED") : ("COMPLIANCE_EXPIRING", "COMPLIANCE_EXPIRING");

            var recipients = await audience.ByRolesAsync(d.OrgId, ComplianceRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(d.OrgId, "compliance_document", d.Id, key, ct)) continue;

            sent += await DispatchAsync(template, NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]  = FirstNameOf(m.Name),
                ["DocName"]    = d.DocName,
                ["ExpiryText"] = RelativeDayText(days),
                ["ExpiryDate"] = d.ExpiryDate!.Value.ToString("dd MMM yyyy"),
                ["OrgId"]      = d.OrgId.ToString(),
                ["EntityId"]   = d.Id.ToString(),
                ["Link"]       = $"{FrontendBaseUrl}/compliance",
            }, ct);
        }
        return sent;
    }

    // ── Delivery milestones: overdue ────────────────────────────────────────────
    private async Task<int> ScanDeliveryMilestonesAsync(DateOnly today, CancellationToken ct)
    {
        var milestones = await db.DeliveryMilestones
            .AsNoTracking()
            .Where(m => m.CompletedAt == null && m.DueDate != null && m.DueDate < today)
            .Select(m => new { m.Id, m.OrgId, m.OrderId, m.Title, m.DueDate })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var ms in milestones)
        {
            var days = ms.DueDate!.Value.DayNumber - today.DayNumber;

            var recipients = await audience.ByRolesAsync(ms.OrgId, DeliveryRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(ms.OrgId, "delivery_milestone", ms.Id, "DELIVERY_OVERDUE", ct)) continue;

            sent += await DispatchAsync("DELIVERY_OVERDUE", NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]      = FirstNameOf(m.Name),
                ["MilestoneTitle"] = ms.Title,
                ["DueText"]        = RelativeDayText(days),
                ["DueDate"]        = ms.DueDate!.Value.ToString("dd MMM yyyy"),
                ["OrgId"]          = ms.OrgId.ToString(),
                ["EntityId"]       = ms.OrderId.ToString(),
                ["Link"]           = $"{FrontendBaseUrl}/orders/{ms.OrderId}",
            }, ct);
        }
        return sent;
    }

    // ── EMD: held too long (stuck working capital) ──────────────────────────────
    private async Task<int> ScanEmdAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var cutoff = today.AddDays(-opt.EmdStuckDays);
        var emds = await db.EmdPayments
            .AsNoTracking()
            .Where(e => e.Status == "held" && e.PaymentDate <= cutoff)
            .Select(e => new { e.Id, e.OrgId, e.TenderTitle, e.Amount, e.PaymentDate })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var e in emds)
        {
            var heldDays = today.DayNumber - e.PaymentDate.DayNumber;

            var recipients = await audience.ByRolesAsync(e.OrgId, FinanceRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(e.OrgId, "emd", e.Id, "EMD_STUCK", ct)) continue;

            sent += await DispatchAsync("EMD_STUCK", NotificationCategory.Information, recipients, m => new()
            {
                ["FirstName"]   = FirstNameOf(m.Name),
                ["TenderTitle"] = e.TenderTitle ?? "a tender",
                ["Amount"]      = e.Amount.ToString("N0"),
                ["HeldDays"]    = heldDays,
                ["PaymentDate"] = e.PaymentDate.ToString("dd MMM yyyy"),
                ["OrgId"]       = e.OrgId.ToString(),
                ["EntityId"]    = e.Id.ToString(),
                ["Link"]        = $"{FrontendBaseUrl}/payments",
            }, ct);
        }
        return sent;
    }

    // ── EMD instrument: original paper still not couriered ──────────────────────
    //
    // The one that actually loses bids. An EMD furnished as a DD/BG/FDR is only valid once the
    // ORIGINAL reaches the buyer, and no amount of "we paid it" helps if the envelope is still
    // on someone's desk. Fires on an EMD whose cut-off is inside the lead window and which has
    // no live outbound consignment against it.
    private async Task<int> ScanEmdDispatchDueAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.EmdDispatchLeadDays);
        var instrumentModes = EmdModes.Instrument.ToArray();
        // returned/lost consignments do NOT count as sent — the buyer never got them, so the
        // EMD is still effectively un-dispatched and the warning must keep applying.
        var failedStatuses = DispatchStatuses.Failed.ToArray();

        var rows = await db.EmdPayments
            .AsNoTracking()
            .Where(e => e.BidId != null && e.PaymentMode != null && instrumentModes.Contains(e.PaymentMode))
            .Join(db.Bids, e => e.BidId, b => b.Id, (e, b) => new { Emd = e, Bid = b })
            .Where(x => x.Bid.StatusCategory == "open"
                     && x.Bid.EmdRequirement == EmdRequirements.Required)
            .Where(x => (x.Emd.DueDate ?? x.Bid.DueDate) != null
                     && (x.Emd.DueDate ?? x.Bid.DueDate) <= horizon)
            .Where(x => !db.BidDispatches.Any(d =>
                        d.BidId == x.Bid.Id
                     && d.Purpose == "emd_instrument"
                     && d.Direction == "outbound"
                     && !failedStatuses.Contains(d.Status)))
            .Select(x => new
            {
                EmdId = x.Emd.Id,
                x.Emd.OrgId,
                x.Emd.PaymentMode,
                BidId = x.Bid.Id,
                BidTitle = x.Bid.Title,
                x.Bid.AssignedTo,
                Cutoff = x.Emd.DueDate ?? x.Bid.DueDate,
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var r in rows)
        {
            var days = r.Cutoff!.Value.DayNumber - today.DayNumber;

            var recipients = r.AssignedTo is Guid a
                ? await OneAsync(a, ct)
                : await audience.ByRolesAsync(r.OrgId, BidRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(r.OrgId, "emd", r.EmdId, "EMD_DISPATCH_DUE", ct)) continue;

            sent += await DispatchAsync("EMD_DISPATCH_DUE", NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]       = FirstNameOf(m.Name),
                ["BidTitle"]        = r.BidTitle,
                ["InstrumentLabel"] = EmdModes.Label(r.PaymentMode),
                ["DeliverBy"]       = r.Cutoff!.Value.ToString("dd MMM yyyy"),
                ["DueText"]         = RelativeDayText(days),
                ["OrgId"]           = r.OrgId.ToString(),
                ["EntityId"]        = r.BidId.ToString(),
                ["Link"]            = $"{FrontendBaseUrl}/bids/{r.BidId}?tab=emd",
            }, ct);
        }
        return sent;
    }

    // ── EMD courier: sent, but nobody has confirmed it landed ───────────────────
    private async Task<int> ScanEmdDeliveryOverdueAsync(DateOnly today, CancellationToken ct)
    {
        var inFlight = DispatchStatuses.InFlight.ToArray();

        var rows = await db.BidDispatches
            .AsNoTracking()
            .Where(d => inFlight.Contains(d.Status) && d.DeliveredOn == null)
            // Either the courier blew its own promised date, or the tender's cut-off has passed
            // with the packet still in transit — the second is the graver of the two.
            .Where(d => (d.ExpectedDeliveryOn != null && d.ExpectedDeliveryOn < today)
                     || (d.DeliverBy != null && d.DeliverBy < today))
            .Join(db.Bids, d => d.BidId, b => b.Id, (d, b) => new { D = d, Bid = b })
            .Select(x => new
            {
                DispatchId = x.D.Id,
                x.D.OrgId,
                BidId = x.Bid.Id,
                BidTitle = x.Bid.Title,
                x.Bid.AssignedTo,
                x.D.CourierName,
                x.D.TrackingNumber,
                x.D.DispatchedOn,
                Expected = x.D.ExpectedDeliveryOn ?? x.D.DeliverBy,
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var r in rows)
        {
            var days = r.Expected.HasValue ? r.Expected.Value.DayNumber - today.DayNumber : 0;

            var recipients = r.AssignedTo is Guid a
                ? await OneAsync(a, ct)
                : await audience.ByRolesAsync(r.OrgId, BidRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(r.OrgId, "bid_dispatch", r.DispatchId, "EMD_DELIVERY_OVERDUE", ct)) continue;

            sent += await DispatchAsync("EMD_DELIVERY_OVERDUE", NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]      = FirstNameOf(m.Name),
                ["BidTitle"]       = r.BidTitle,
                ["CourierName"]    = r.CourierName ?? "the courier",
                ["TrackingNumber"] = r.TrackingNumber ?? "—",
                ["DispatchedOn"]   = r.DispatchedOn?.ToString("dd MMM yyyy") ?? "—",
                ["ExpectedText"]   = RelativeDayText(days),
                ["OrgId"]          = r.OrgId.ToString(),
                ["EntityId"]       = r.BidId.ToString(),
                ["Link"]           = $"{FrontendBaseUrl}/bids/{r.BidId}?tab=emd",
            }, ct);
        }
        return sent;
    }

    // ── EMD instrument: bank guarantee about to lapse ───────────────────────────
    //
    // Goes to finance rather than the bid owner: extending a BG is a bank errand, and it is the
    // same audience that already fields EMD_STUCK. Refunded/forfeited EMDs are excluded — the
    // instrument is spent, its expiry is nobody's problem.
    private async Task<int> ScanEmdInstrumentExpiryAsync(DateOnly today, DeadlineScanOptions opt, CancellationToken ct)
    {
        var horizon = today.AddDays(opt.EmdExpiryLeadDays);
        var expiringModes = EmdModes.Expiring.ToArray();

        var rows = await db.EmdPayments
            .AsNoTracking()
            .Where(e => e.ValidUntil != null && e.ValidUntil <= horizon
                     && e.PaymentMode != null && expiringModes.Contains(e.PaymentMode)
                     && e.Status != "refunded" && e.Status != "forfeited"
                     && e.BidId != null)
            .Join(db.Bids, e => e.BidId, b => b.Id, (e, b) => new { Emd = e, Bid = b })
            .Where(x => x.Bid.StatusCategory == "open")
            .Select(x => new
            {
                EmdId = x.Emd.Id,
                x.Emd.OrgId,
                x.Emd.PaymentMode,
                x.Emd.Amount,
                x.Emd.ValidUntil,
                BidId = x.Bid.Id,
                BidTitle = x.Bid.Title,
            })
            .ToListAsync(ct);

        var sent = 0;
        foreach (var r in rows)
        {
            var days = r.ValidUntil!.Value.DayNumber - today.DayNumber;

            var recipients = await audience.ByRolesAsync(r.OrgId, FinanceRoles, null, ct);
            if (recipients.Count == 0) continue;
            if (!await TryClaimAsync(r.OrgId, "emd", r.EmdId, "EMD_BG_EXPIRING", ct)) continue;

            sent += await DispatchAsync("EMD_BG_EXPIRING", NotificationCategory.Transactional, recipients, m => new()
            {
                ["FirstName"]       = FirstNameOf(m.Name),
                ["BidTitle"]        = r.BidTitle,
                ["InstrumentLabel"] = EmdModes.Label(r.PaymentMode),
                ["Amount"]          = r.Amount.ToString("N0"),
                ["ValidUntil"]      = r.ValidUntil!.Value.ToString("dd MMM yyyy"),
                ["ExpiryText"]      = RelativeDayText(days),
                ["OrgId"]           = r.OrgId.ToString(),
                ["EntityId"]        = r.BidId.ToString(),
                ["Link"]            = $"{FrontendBaseUrl}/bids/{r.BidId}?tab=emd",
            }, ct);
        }
        return sent;
    }

    // ── plumbing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically reserve (entity, milestone). Returns true only if THIS call inserted the
    /// ledger row — i.e. the reminder has not been sent before. ExecuteSql runs in autocommit
    /// (no ambient transaction, no change-tracker interaction), so it commits before dispatch.
    /// </summary>
    private async Task<bool> TryClaimAsync(Guid orgId, string entityType, Guid entityId, string key, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO notification_reminders (org_id, entity_type, entity_id, reminder_key)
            VALUES ({orgId}, {entityType}, {entityId}, {key})
            ON CONFLICT (entity_type, entity_id, reminder_key) DO NOTHING", ct);
        return rows == 1;
    }

    private async Task<int> DispatchAsync(
        string templateCode, string category,
        IReadOnlyList<NotificationAudienceMember> recipients,
        Func<NotificationAudienceMember, Dictionary<string, object>> payloadFor,
        CancellationToken ct)
    {
        var sent = 0;
        foreach (var m in recipients)
        {
            var channels = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.InApp, m.UserId.ToString()),
            };
            if (!string.IsNullOrWhiteSpace(m.Email))
                channels.Add(new NotificationRecipientDto(NotificationChannel.Email, m.Email!));

            try
            {
                await publisher.SendAsync(new SendNotificationDto(
                    Category:     category,
                    TemplateCode: templateCode,
                    UserId:       m.UserId,
                    Payload:      payloadFor(m),
                    Recipients:   channels), ct);
                sent++;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "[DeadlineScan] Failed to send {Template} to user {UserId}", templateCode, m.UserId);
            }
        }
        return sent;
    }

    private async Task<IReadOnlyList<NotificationAudienceMember>> OneAsync(Guid userId, CancellationToken ct)
    {
        var m = await audience.ByUserAsync(userId, ct);
        return m is null ? [] : [m];
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];

    /// <summary>Human "in 2 days" / "today" / "3 days ago" relative to the scan date.</summary>
    private static string RelativeDayText(int days) => days switch
    {
        < -1 => $"{-days} days ago",
        -1   => "yesterday",
        0    => "today",
        1    => "tomorrow",
        _    => $"in {days} days",
    };
}
