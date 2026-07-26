using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class JoinRequestService(
    BffDbContext db,
    INotificationPublisher notifications,
    IConfiguration config,
    ILogger<JoinRequestService> log) : IJoinRequestService
{
    /// <summary>
    /// Roles an approver may grant. <c>owner</c> is absent on purpose: the org already has one
    /// (<c>organizations.owned_by</c>), and letting an approval mint a second would create two
    /// members neither of whom can be removed by <c>RemoveMemberAsync</c>.
    /// </summary>
    private static readonly string[] GrantableRoles =
        ["admin", "bid_manager", "finance", "sales", "viewer"];

    /// <summary>Decisions older than this drop off the requester's own list. Long enough that
    /// someone who applied on Friday still sees the outcome, short enough that the onboarding
    /// page is not haunted by a rejection from months ago.</summary>
    private static readonly TimeSpan DecisionVisibility = TimeSpan.FromDays(30);

    public async Task<JoinRequestResultDto> RequestAsync(Guid userId, CreateJoinRequestDto dto, CancellationToken ct = default)
    {
        var org = await db.Organizations
            .Where(o => o.Id == dto.OrgId && o.IsActive)
            .Select(o => new { o.Id, o.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        // Already in? Say so rather than queueing a request an admin would have to dismiss.
        var alreadyMember = await db.OrgMembers
            .AnyAsync(m => m.OrgId == org.Id && m.UserId == userId && m.Status == "active", ct);
        if (alreadyMember)
            throw new InvalidOperationException("ALREADY_MEMBER");

        // Idempotent: the client's "Request to join" button is easy to double-tap, and a retry
        // after a dropped response must not stack rows. The partial unique index would reject
        // the second insert anyway — returning the live row turns a 500 into the right answer.
        var existing = await db.OrgJoinRequests
            .FirstOrDefaultAsync(r => r.OrgId == org.Id && r.UserId == userId && r.Status == "pending", ct);
        if (existing is not null)
            return new JoinRequestResultDto(existing.Id, org.Id, org.Name, existing.Status);

        var request = new OrgJoinRequest
        {
            Id        = Guid.NewGuid(),
            OrgId     = org.Id,
            UserId    = userId,
            Status    = "pending",
            Message   = NullIfBlank(dto.Message),
            CreatedAt = DateTime.UtcNow,
        };
        db.OrgJoinRequests.Add(request);
        await db.SaveChangesAsync(ct);

        await NotifyApproversAsync(request, org.Id, org.Name, userId, ct);

        return new JoinRequestResultDto(request.Id, org.Id, org.Name, request.Status);
    }

    public async Task<IReadOnlyList<MyJoinRequestDto>> GetMineAsync(Guid userId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - DecisionVisibility;

        return await (
            from r in db.OrgJoinRequests
            where r.UserId == userId
                  && (r.Status == "pending" || (r.DecidedAt != null && r.DecidedAt > cutoff))
            join o in db.Organizations on r.OrgId equals o.Id
            orderby r.CreatedAt descending
            select new MyJoinRequestDto(
                r.Id, r.OrgId, o.Name, o.LogoUrl, r.Status, r.Role, r.CreatedAt, r.DecidedAt)
        ).ToListAsync(ct);
    }

    public async Task CancelAsync(Guid userId, Guid requestId, CancellationToken ct = default)
    {
        var request = await db.OrgJoinRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Join request not found.");

        // Already decided — nothing to withdraw. Silent rather than an error: the user's intent
        // ("I don't want this pending any more") is already satisfied.
        if (request.Status != "pending") return;

        request.Status    = "cancelled";
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrgJoinRequestDto>> GetPendingForOrgAsync(Guid orgId, Guid requestingUserId, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, requestingUserId, ct);

        return await (
            from r in db.OrgJoinRequests
            where r.OrgId == orgId && r.Status == "pending"
            join u in db.Users on r.UserId equals u.Id
            orderby r.CreatedAt
            select new OrgJoinRequestDto(
                r.Id, r.UserId, u.Name, u.Email, u.AvatarUrl, r.Message, r.CreatedAt)
        ).ToListAsync(ct);
    }

    public async Task<OrgMemberDto> ApproveAsync(
        Guid orgId, Guid requestId, Guid approverId, ApproveJoinRequestDto dto, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, approverId, ct);

        var role = (dto.Role ?? string.Empty).Trim().ToLowerInvariant();
        if (!GrantableRoles.Contains(role))
            throw new InvalidOperationException("INVALID_ROLE");

        var request = await LoadPendingAsync(orgId, requestId, ct);

        // Same shape as accepting an invite: reactivate a suspended membership rather than
        // inserting a second row, which the UNIQUE (org_id, user_id) index would reject.
        var member = await db.OrgMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == request.UserId, ct);

        if (member is null)
        {
            member = new OrgMember
            {
                OrgId     = orgId,
                UserId    = request.UserId,
                Role      = role,
                Status    = "active",
                InvitedBy = approverId,
                JoinedAt  = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
            };
            db.OrgMembers.Add(member);
        }
        else
        {
            member.Status   = "active";
            member.Role     = role;
            member.JoinedAt = DateTime.UtcNow;
        }

        request.Status    = "approved";
        request.Role      = role;
        request.DecidedBy = approverId;
        request.DecidedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await NotifyDecisionAsync(request, orgId, approverId, approved: true, ct);

        // Reload through the navigation so the DTO carries the user's name/email even on the
        // insert path, where `member.User` was never populated.
        await db.Entry(member).Reference(m => m.User).LoadAsync(ct);
        return MapMember(member);
    }

    public async Task RejectAsync(Guid orgId, Guid requestId, Guid deciderId, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, deciderId, ct);

        var request = await LoadPendingAsync(orgId, requestId, ct);

        request.Status    = "rejected";
        request.DecidedBy = deciderId;
        request.DecidedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await NotifyDecisionAsync(request, orgId, deciderId, approved: false, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<OrgJoinRequest> LoadPendingAsync(Guid orgId, Guid requestId, CancellationToken ct)
    {
        var request = await db.OrgJoinRequests
            .FirstOrDefaultAsync(r => r.Id == requestId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Join request not found.");

        // Scoped by orgId above, so an admin of org A cannot decide org B's queue by id.
        if (request.Status != "pending")
            throw new InvalidOperationException("REQUEST_ALREADY_DECIDED");

        return request;
    }

    /// <summary>Deciding who joins is an owner/admin power — the same gate the invite flow uses.</summary>
    private async Task RequireRoleAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        var role = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active")
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null || (role != "owner" && role != "admin"))
            throw new UnauthorizedAccessException("Insufficient permissions.");
    }

    /// <summary>
    /// One JOIN_REQUEST notification per approver, not one addressed to all of them — the
    /// template greets by name, so a single fan-out message would call every admin but one by
    /// the wrong name. Never throws: the request row is already committed.
    /// </summary>
    private async Task NotifyApproversAsync(OrgJoinRequest request, Guid orgId, string orgName, Guid requesterId, CancellationToken ct)
    {
        try
        {
            var requester = await db.Users
                .Where(u => u.Id == requesterId)
                .Select(u => new { u.Name, u.Email })
                .FirstOrDefaultAsync(ct);

            var approvers = await db.OrgMembers
                .Where(m => m.OrgId == orgId && m.Status == "active" && (m.Role == "owner" || m.Role == "admin"))
                .Select(m => new { m.UserId, m.User.Name, m.User.Email })
                .ToListAsync(ct);

            if (approvers.Count == 0)
            {
                // An org with no active owner or admin cannot approve anyone. Nothing to send,
                // but worth a line — it means the request will sit in a queue nobody can see.
                log.LogWarning(
                    "Join request {RequestId} for org {OrgId} has no active owner/admin to notify.",
                    request.Id, orgId);
                return;
            }

            var link = $"{FrontendBase()}/team";

            foreach (var approver in approvers)
            {
                var recipients = new List<NotificationRecipientDto>
                {
                    new(NotificationChannel.InApp, approver.UserId.ToString()),
                };
                if (!string.IsNullOrWhiteSpace(approver.Email))
                    recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, approver.Email));

                await notifications.SendAsync(new SendNotificationDto(
                    Category:     NotificationCategory.Transactional,
                    TemplateCode: "JOIN_REQUEST",
                    UserId:       approver.UserId,
                    Payload: new Dictionary<string, object>
                    {
                        ["FirstName"]      = FirstNameOr(approver.Name, "there"),
                        ["RequesterName"]  = requester?.Name ?? "Someone",
                        ["RequesterEmail"] = requester?.Email ?? string.Empty,
                        ["OrgName"]        = orgName,
                        ["Message"]        = request.Message ?? string.Empty,
                        ["Link"]           = link,
                    },
                    Recipients: recipients), ct);
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "JOIN_REQUEST notification failed for request {RequestId}.", request.Id);
        }
    }

    /// <summary>Tell the requester what was decided. Never throws — the decision is committed.</summary>
    private async Task NotifyDecisionAsync(OrgJoinRequest request, Guid orgId, Guid deciderId, bool approved, CancellationToken ct)
    {
        try
        {
            var requester = await db.Users
                .Where(u => u.Id == request.UserId)
                .Select(u => new { u.Name, u.Email })
                .FirstOrDefaultAsync(ct);

            var orgName = await db.Organizations
                .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? string.Empty;

            var deciderName = await db.Users
                .Where(u => u.Id == deciderId).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "An admin";

            var recipients = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.InApp, request.UserId.ToString()),
            };
            if (!string.IsNullOrWhiteSpace(requester?.Email))
                recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, requester!.Email));

            // Approved → their new workspace. Rejected → back to onboarding, where creating
            // their own workspace is still available; a dead link would be a worse ending.
            var link = approved ? $"{FrontendBase()}/dashboard" : $"{FrontendBase()}/onboarding/company";

            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: approved ? "JOIN_REQUEST_APPROVED" : "JOIN_REQUEST_REJECTED",
                UserId:       request.UserId,
                Payload: new Dictionary<string, object>
                {
                    ["FirstName"]    = FirstNameOr(requester?.Name, "there"),
                    ["OrgName"]      = orgName,
                    ["Role"]         = request.Role ?? string.Empty,
                    ["ApproverName"] = deciderName,
                    ["Link"]         = link,
                },
                Recipients: recipients), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "Join request decision notification failed for request {RequestId}.", request.Id);
        }
    }

    private string FrontendBase()
        => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    private static OrgMemberDto MapMember(OrgMember m) => new(
        m.Id, m.UserId,
        m.User?.Name ?? string.Empty,
        m.User?.Email ?? string.Empty,
        m.User?.AvatarUrl,
        m.Role, m.Department, m.Status,
        m.JoinedAt,
        0, null, 0);

    private static string FirstNameOr(string? fullName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return fallback;
        var space = fullName.IndexOf(' ');
        var first = (space < 0 ? fullName : fullName[..space]).Trim();
        return first.Length == 0 ? fallback : first;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
