using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Buyer-access requests. See <see cref="IBuyerRequestService"/> for the self-serve-request /
/// operator-approve split and why it exists.
/// </summary>
public class BuyerRequestService(
    BffDbContext db,
    IOrganizationService orgs,
    INotificationPublisher notifications,
    INotificationAudienceResolver audience,
    IConfiguration config,
    ILogger<BuyerRequestService> log) : IBuyerRequestService
{
    /// <summary>Who inside the org may raise a request. Not a viewer or a sales rep — turning the
    /// whole workspace into a government publisher is an owner/admin decision.</summary>
    private static readonly string[] Requesters = ["owner", "admin"];

    /// <summary>The org roles told when a request is decided.</summary>
    private static readonly string[] DecisionAudience = ["owner", "admin"];

    private const string FrontendSettings = "/settings";
    private const string FrontendBuyer = "/buyer/tenders";

    // ── Org side ─────────────────────────────────────────────────────────────

    public async Task<BuyerRequestDto> RequestAsync(
        Guid orgId, Guid userId, RequestBuyerAccessDto dto, CancellationToken ct = default)
    {
        await RequireOrgRoleAsync(orgId, userId, ct);

        if (string.IsNullOrWhiteSpace(dto.Justification))
            throw new InvalidOperationException(
                "A justification is required — an approval is a judgement, and it needs something to judge.");

        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        // Already publishing? Say so rather than queueing a request an operator would just dismiss.
        if (org.OrgType is "buyer" or "both")
            throw new InvalidOperationException("ALREADY_BUYER");

        // Idempotent: a double-tapped button or a retry after a dropped response must not stack
        // rows. The partial unique index would reject the second insert anyway — returning the live
        // row turns a would-be 500 into the right answer.
        var existing = await db.OrgBuyerRequests
            .Include(r => r.Requester)
            .FirstOrDefaultAsync(r => r.OrgId == orgId && r.Status == "pending", ct);
        if (existing is not null)
            return Map(existing);

        var request = new OrgBuyerRequest
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            RequestedBy = userId,
            Status = "pending",
            EntityType = NullIfBlank(dto.EntityType),
            Ministry = NullIfBlank(dto.Ministry),
            Department = NullIfBlank(dto.Department),
            Office = NullIfBlank(dto.Office),
            ProcuringEntityCode = NullIfBlank(dto.ProcuringEntityCode),
            Justification = dto.Justification.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        db.OrgBuyerRequests.Add(request);
        await db.SaveChangesAsync(ct);

        await NotifyOperatorAsync(org, request, userId, ct);

        log.LogInformation(
            "Buyer request {RequestId} raised for org {OrgId} ({OrgName}) by {UserId}",
            request.Id, orgId, org.Name, userId);

        // Re-read with the requester loaded, so the returned DTO carries the name.
        return Map(await db.OrgBuyerRequests.Include(r => r.Requester).FirstAsync(r => r.Id == request.Id, ct));
    }

    public async Task<BuyerRequestDto?> GetCurrentAsync(Guid orgId, CancellationToken ct = default)
    {
        // Most recent request of any status: a pending one drives the "under review" state, a
        // decided one drives "approved"/"rejected — reason: …" or the option to ask again.
        var request = await db.OrgBuyerRequests
            .Include(r => r.Requester)
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return request is null ? null : Map(request);
    }

    public async Task<bool> CancelAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        await RequireOrgRoleAsync(orgId, userId, ct);

        var request = await db.OrgBuyerRequests
            .FirstOrDefaultAsync(r => r.OrgId == orgId && r.Status == "pending", ct);
        if (request is null) return false;

        request.Status = "cancelled";
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    // ── Operator side ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BuyerRequestQueueItemDto>> ListAsync(string? status, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(status) ? "pending" : status.Trim().ToLowerInvariant();

        return await db.OrgBuyerRequests
            .Where(r => r.Status == filter)
            .OrderBy(r => r.CreatedAt)               // oldest first — first asked, first reviewed
            .Select(r => new BuyerRequestQueueItemDto(
                r.Id, r.OrgId, r.Organization.Name, r.Organization.Gstin, r.Status,
                r.EntityType, r.Ministry, r.Department, r.Office, r.ProcuringEntityCode,
                r.Justification, r.Requester.Name, r.Requester.Email, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<BuyerRequestDto?> ApproveAsync(
        Guid requestId, ApproveBuyerRequestDto dto, CancellationToken ct = default)
    {
        var request = await db.OrgBuyerRequests
            .Include(r => r.Requester)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return null;

        if (request.Status != "pending")
            throw new InvalidOperationException("NOT_PENDING");

        // The conversion is not re-implemented here. It runs through the exact same path an operator
        // uses to provision a department directly — which is what keeps "requested and approved" and
        // "provisioned outright" from drifting into two subtly different notions of "a buyer". The
        // claimed identity flows straight in, and SetOrgTypeAsync writes the org-level audit event.
        var result = await orgs.SetOrgTypeAsync(request.OrgId, new SetOrgTypeDto(
            OrgType: dto.OrgType,
            EntityType: request.EntityType,
            Ministry: request.Ministry,
            Department: request.Department,
            Office: request.Office,
            ProcuringEntityCode: request.ProcuringEntityCode,
            VerificationNote: $"Approved buyer request {request.Id}. {dto.DecisionNote}".Trim()), ct);

        if (result is null)
        {
            // The org was deleted between the request and the decision. Nothing to convert; close
            // the request so it leaves the queue rather than sitting there forever.
            request.Status = "rejected";
            request.DecisionNote = "Organization no longer exists.";
            request.DecidedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Map(request);
        }

        request.Status = "approved";
        request.DecisionNote = NullIfBlank(dto.DecisionNote);
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await NotifyDecisionAsync(request, "BUYER_REQUEST_APPROVED", FrontendBuyer, ct);

        log.LogWarning(
            "Buyer request {RequestId} APPROVED — org {OrgId} converted to {OrgType}",
            request.Id, request.OrgId, dto.OrgType);

        return Map(request);
    }

    public async Task<BuyerRequestDto?> RejectAsync(
        Guid requestId, RejectBuyerRequestDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.DecisionNote))
            throw new InvalidOperationException(
                "A reason is required to reject — the org is told why so it can address it and reapply.");

        var request = await db.OrgBuyerRequests
            .Include(r => r.Requester)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);
        if (request is null) return null;

        if (request.Status != "pending")
            throw new InvalidOperationException("NOT_PENDING");

        request.Status = "rejected";
        request.DecisionNote = dto.DecisionNote.Trim();
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await NotifyDecisionAsync(request, "BUYER_REQUEST_REJECTED", FrontendSettings, ct);

        log.LogInformation("Buyer request {RequestId} rejected for org {OrgId}", request.Id, request.OrgId);

        return Map(request);
    }

    // ── Notifications ────────────────────────────────────────────────────────

    /// <summary>
    /// Emails the configured operations address that a request is waiting.
    /// </summary>
    /// <remarks>
    /// There is no operator USER in this system — operators act via an API key — so this goes to a
    /// plain address from <c>BuyerRequests:NotifyEmail</c>, with <c>UserId = null</c>. If the address
    /// is not configured, the queue is still there to poll; we log and move on rather than fail the
    /// request over a missing ops mailbox.
    /// </remarks>
    private async Task NotifyOperatorAsync(Organization org, OrgBuyerRequest request, Guid requesterId, CancellationToken ct)
    {
        var opsEmail = config["BuyerRequests:NotifyEmail"];
        if (string.IsNullOrWhiteSpace(opsEmail))
        {
            log.LogInformation(
                "Buyer request {RequestId} raised but BuyerRequests:NotifyEmail is not configured — "
                + "no operator email sent (the /internal queue still lists it)", request.Id);
            return;
        }

        try
        {
            var requester = await audience.ByUserAsync(requesterId, ct);
            await notifications.SendAsync(new SendNotificationDto(
                Category: "Transactional",
                TemplateCode: "BUYER_REQUEST_SUBMITTED",
                UserId: null,
                Payload: new Dictionary<string, object>
                {
                    ["OrgName"] = org.Name,
                    ["RequesterName"] = requester?.Name ?? "(unknown)",
                    ["RequesterEmail"] = requester?.Email ?? "(unknown)",
                    ["EntityType"] = request.EntityType ?? "—",
                    ["Ministry"] = request.Ministry ?? "",
                    ["Department"] = request.Department ?? "",
                    ["Justification"] = request.Justification,
                    ["RequestId"] = request.Id.ToString(),
                },
                Recipients: [new NotificationRecipientDto("Email", opsEmail)]), ct);
        }
        catch (Exception ex)
        {
            // A missing operator email must never fail the org's request — the row is committed and
            // the queue endpoint will surface it regardless.
            log.LogWarning(ex, "Operator notification failed for buyer request {RequestId}", request.Id);
        }
    }

    private async Task NotifyDecisionAsync(OrgBuyerRequest request, string template, string actionUrl, CancellationToken ct)
    {
        try
        {
            var org = await db.Organizations
                .Where(o => o.Id == request.OrgId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct) ?? "your organization";

            // Notify the stable audience (owner/admins), not just the requester who may have left.
            var recipients = await audience.ByRolesAsync(request.OrgId, DecisionAudience, ct: ct);
            if (recipients.Count == 0) return;

            foreach (var r in recipients)
            {
                await notifications.SendAsync(new SendNotificationDto(
                    Category: "Transactional",
                    TemplateCode: template,
                    UserId: r.UserId,
                    Payload: new Dictionary<string, object>
                    {
                        ["FirstName"] = FirstName(r.Name),
                        ["OrgName"] = org,
                        ["DecisionNote"] = request.DecisionNote ?? "",
                        ["Link"] = actionUrl,
                    },
                    Recipients: BuildRecipients(r, template)), ct);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Decision notification failed for buyer request {RequestId}", request.Id);
        }
    }

    private static IReadOnlyList<NotificationRecipientDto> BuildRecipients(NotificationAudienceMember m, string template)
    {
        var list = new List<NotificationRecipientDto> { new("InApp", m.UserId.ToString()) };
        // Both decision templates have an Email variant; send it when we have an address.
        if (!string.IsNullOrWhiteSpace(m.Email))
            list.Add(new NotificationRecipientDto("Email", m.Email));
        return list;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task RequireOrgRoleAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        var role = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active")
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null || !Requesters.Contains(role))
            throw new UnauthorizedAccessException(
                "Only an owner or admin can request buyer access for the organization.");
    }

    private static BuyerRequestDto Map(OrgBuyerRequest r) => new(
        r.Id, r.OrgId, r.Status,
        r.EntityType, r.Ministry, r.Department, r.Office, r.ProcuringEntityCode,
        r.Justification, r.DecisionNote,
        r.Requester?.Name ?? "(unknown)",
        r.DecidedAt, r.CreatedAt);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string FirstName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "there" : name.Split(' ')[0];
}
