using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class OrganizationService(
    BffDbContext db,
    IUserRepository userRepo,
    INotificationPublisher notifications,
    IConfiguration config,
    ILogger<OrganizationService> log) : IOrganizationService
{
    public async Task<OrgDetailDto> CreateAsync(Guid ownerId, CreateOrgDto dto, CancellationToken ct = default)
    {
        var org = new Organization
        {
            OwnedBy           = ownerId,
            Name              = dto.Name,
            Slug              = dto.Slug,
            Gstin             = dto.Gstin,
            Pan               = dto.Pan,
            Industry          = dto.Industry,
            CompanySize       = dto.CompanySize,
            RegisteredAddress = dto.RegisteredAddress,
            City              = dto.City,
            State             = dto.State,
            Pincode           = dto.Pincode,
            Website           = dto.Website,
            GemSellerId       = dto.GemSellerId,
            PrimaryCategory   = dto.PrimaryCategory,
        };

        db.Organizations.Add(org);

        var ownerMember = new OrgMember
        {
            OrgId    = org.Id,
            UserId   = ownerId,
            Role     = "owner",
            Status   = "active",
            JoinedAt = DateTime.UtcNow,
        };
        db.OrgMembers.Add(ownerMember);

        await db.SaveChangesAsync(ct);

        return await GetAsync(org.Id, ownerId, ct);
    }

    public async Task<OrgDetailDto> GetAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var org = await db.Organizations
            .Include(o => o.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(o => o.Id == orgId, ct)
            ?? throw new KeyNotFoundException("Organization not found.");

        var role = org.Members.FirstOrDefault(m => m.UserId == userId)?.Role ?? "viewer";
        var stats = await ComputeMemberStatsAsync(orgId, ct);
        return MapToDetail(org, role, stats);
    }

    public async Task<OrgDetailDto> UpdateAsync(Guid orgId, Guid userId, UpdateOrgDto dto, CancellationToken ct = default)
    {
        var org = await LoadOrgAsync(orgId, ct);
        await RequireRoleAsync(orgId, userId, ["owner", "admin"], ct);

        if (dto.Name       is not null) org.Name              = dto.Name;
        if (dto.Slug       is not null) org.Slug              = dto.Slug;
        if (dto.Gstin      is not null) org.Gstin             = dto.Gstin;
        if (dto.Pan        is not null) org.Pan               = dto.Pan;
        if (dto.Industry   is not null) org.Industry          = dto.Industry;
        if (dto.CompanySize is not null) org.CompanySize      = dto.CompanySize;
        if (dto.RegisteredAddress is not null) org.RegisteredAddress = dto.RegisteredAddress;
        if (dto.City       is not null) org.City              = dto.City;
        if (dto.State      is not null) org.State             = dto.State;
        if (dto.Pincode    is not null) org.Pincode           = dto.Pincode;
        if (dto.Website    is not null) org.Website           = dto.Website;
        if (dto.GemSellerId is not null) org.GemSellerId     = dto.GemSellerId;
        if (dto.PrimaryCategory is not null) org.PrimaryCategory = dto.PrimaryCategory;
        if (dto.LogoUrl    is not null) org.LogoUrl           = dto.LogoUrl;

        await db.SaveChangesAsync(ct);
        return await GetAsync(orgId, userId, ct);
    }

    public async Task DeactivateAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        var org = await LoadOrgAsync(orgId, ct);
        await RequireRoleAsync(orgId, userId, ["owner"], ct);

        org.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrgMemberDto>> GetMembersAsync(Guid orgId, CancellationToken ct = default)
    {
        var members = await db.OrgMembers
            .Include(m => m.User)
            .Where(m => m.OrgId == orgId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);

        var stats = await ComputeMemberStatsAsync(orgId, ct);
        return members.Select(m => MapMember(m, stats.GetValueOrDefault(m.UserId))).ToList();
    }

    public async Task<IReadOnlyList<PendingInviteDto>> GetPendingInvitesAsync(Guid orgId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await db.OrganizationInvites
            .Where(i => i.OrgId == orgId && i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new PendingInviteDto(
                i.Id,
                i.Email,
                i.Role,
                i.Department,
                i.InvitedBy,
                db.Users.Where(u => u.Id == i.InvitedBy).Select(u => u.Name).FirstOrDefault(),
                i.ExpiresAt,
                i.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task RevokePendingInviteAsync(Guid orgId, Guid inviteId, Guid requestingUserId, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, requestingUserId, ["owner", "admin"], ct);

        var invite = await db.OrganizationInvites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Invite not found.");

        if (invite.AcceptedAt is not null)
            return; // already accepted/superseded — no-op

        invite.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<InviteMemberResultDto> InviteMemberAsync(Guid orgId, Guid invitedBy, InviteMemberDto dto, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, invitedBy, ["owner", "admin"], ct);

        var email = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(dto));

        var user = await userRepo.FindByEmailAsync(email, ct);

        // ── Case A: invitee already has a user account → grant membership now ──
        if (user is not null)
        {
            var existing = await db.OrgMembers
                .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == user.Id, ct);

            if (existing is not null)
            {
                existing.Status     = "active";
                existing.Role       = dto.Role;
                existing.Department = dto.Department;
                existing.JoinedAt   = DateTime.UtcNow;
            }
            else
            {
                existing = new OrgMember
                {
                    OrgId      = orgId,
                    UserId     = user.Id,
                    Role       = dto.Role,
                    Department = dto.Department,
                    Status     = "active",
                    InvitedBy  = invitedBy,
                    JoinedAt   = DateTime.UtcNow,
                };
                db.OrgMembers.Add(existing);
            }

            await db.SaveChangesAsync(ct);

            existing.User = user;

            // Fire TEAM_INVITATION (Email + InApp) with a deep-link to the org page —
            // they're already a user, so no registration step needed.
            var deepLink = BuildDeepLink(orgId);
            await SendInvitationAsync(invitedBy, user.Id, user.Email, user.Name, orgId, deepLink, ct);

            var stats = await ComputeMemberStatsAsync(orgId, ct);

            return new InviteMemberResultDto(
                Status:       "added",
                Member:       MapMember(existing, stats.GetValueOrDefault(existing.UserId)),
                InvitedEmail: null,
                ExpiresAt:    null);
        }

        // ── Case B: no user with this email → create a pending invite ──────────

        // Cancel any prior pending invite for this email/org by accepting it as expired —
        // simpler than DELETE and preserves history. The partial UNIQUE index on
        // (org_id, email) WHERE accepted_at IS NULL would otherwise block the new INSERT.
        var prior = await db.OrganizationInvites
            .Where(i => i.OrgId == orgId && i.Email == email && i.AcceptedAt == null)
            .ToListAsync(ct);
        foreach (var p in prior)
            p.AcceptedAt = DateTime.UtcNow;     // status "superseded"; the new invite supersedes it

        var (rawToken, tokenHash) = GenerateInviteToken();
        var expiresAt = DateTime.UtcNow.AddDays(7);

        var invite = new OrganizationInvite
        {
            Id         = Guid.NewGuid(),
            OrgId      = orgId,
            Email      = email,
            Role       = dto.Role,
            Department = dto.Department,
            InvitedBy  = invitedBy,
            TokenHash  = tokenHash,
            ExpiresAt  = expiresAt,
            CreatedAt  = DateTime.UtcNow,
        };
        db.OrganizationInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        // Email a registration link carrying the raw token. The recipient registers
        // via POST /api/auth/register with this token in the body; AuthService
        // validates + consumes the invite and joins them to this org.
        var registerLink = BuildRegisterLink(rawToken, email);
        await SendInvitationAsync(invitedBy, recipientUserId: null, email, recipientName: null,
                                  orgId, registerLink, ct);

        return new InviteMemberResultDto(
            Status:       "invited",
            Member:       null,
            InvitedEmail: email,
            ExpiresAt:    expiresAt);
    }

    public async Task<OrgMemberDto> UpdateMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, UpdateMemberDto dto, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, requestingUserId, ["owner", "admin"], ct);

        var member = await db.OrgMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Member not found.");

        var previousRole = member.Role;

        if (dto.Role       is not null) member.Role       = dto.Role;
        if (dto.Department is not null) member.Department = dto.Department;
        if (dto.Status     is not null) member.Status     = dto.Status;

        await db.SaveChangesAsync(ct);

        // Tell the affected member their role changed (not when an admin changes their own).
        if (dto.Role is not null && dto.Role != previousRole && member.UserId != requestingUserId)
            await NotifyMemberRoleChangedAsync(member, requestingUserId, orgId, ct);

        var stats = await ComputeMemberStatsAsync(orgId, ct);
        return MapMember(member, stats.GetValueOrDefault(member.UserId));
    }

    /// <summary>
    /// Notify a member that their org role changed (MEMBER_ROLE_CHANGED, InApp + Email).
    /// Never throws — the role update is already persisted.
    /// </summary>
    private async Task NotifyMemberRoleChangedAsync(OrgMember member, Guid changedById, Guid orgId, CancellationToken ct)
    {
        try
        {
            var orgName = await db.Organizations
                .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
            var changerName = await db.Users
                .Where(u => u.Id == changedById).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "An admin";

            var firstName = FirstName(member.User?.Name);
            if (string.IsNullOrEmpty(firstName)) firstName = "there";

            var recipients = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.InApp, member.UserId.ToString()),
            };
            if (!string.IsNullOrWhiteSpace(member.User?.Email))
                recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, member.User!.Email));

            var frontendBase = (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "MEMBER_ROLE_CHANGED",
                UserId:       member.UserId,
                Payload: new Dictionary<string, object>
                {
                    ["FirstName"]     = firstName,
                    ["OrgName"]       = orgName,
                    ["NewRole"]       = member.Role,
                    ["ChangedByName"] = changerName,
                    ["OrgId"]         = orgId.ToString(),
                    ["Link"]          = $"{frontendBase}/team",
                },
                Recipients: recipients), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "MEMBER_ROLE_CHANGED notification failed for member {MemberId}", member.Id);
        }
    }

    public async Task RemoveMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, requestingUserId, ["owner", "admin"], ct);

        var member = await db.OrgMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Member not found.");

        if (member.Role == "owner")
            throw new InvalidOperationException("Cannot remove the organization owner.");

        db.OrgMembers.Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OrgActivityDto>> GetRecentActivitiesAsync(Guid orgId, Guid requestingUserId, int limit, CancellationToken ct = default)
    {
        await RequireMemberAsync(orgId, requestingUserId, ct);

        limit = Math.Clamp(limit, 1, 100);

        return await db.BidActivities
            .Where(a => a.Bid.OrgId == orgId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(limit)
            .Select(a => new OrgActivityDto(
                a.Id,
                a.ActorId,
                a.Actor.Name,
                a.Action,
                a.FromValue,
                a.ToValue,
                a.Note,
                a.BidId,
                a.Bid.Title,
                a.CreatedAt))
            .ToListAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Per-user aggregates surfaced on <see cref="OrgMemberDto"/>. `default` = all zeros / null win rate.</summary>
    private readonly record struct MemberStats(int ActiveBids, decimal? WinRate, int TasksDone);

    /// <summary>
    /// Compute member stats for the whole org with three grouped queries (no per-member loops),
    /// keyed by <b>user id</b> (bids.assigned_to / checklist done_by reference users, not membership rows).
    /// </summary>
    private async Task<Dictionary<Guid, MemberStats>> ComputeMemberStatsAsync(Guid orgId, CancellationToken ct)
    {
        // Open bids currently assigned to each user (status_category is the generated open|closed column).
        var active = await db.Bids
            .Where(b => b.OrgId == orgId && b.AssignedTo != null && b.StatusCategory == "open")
            .GroupBy(b => b.AssignedTo!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Decided bids (won|lost — 'dropped' is closed but not a decision) per assignee.
        var decided = await db.Bids
            .Where(b => b.OrgId == orgId && b.AssignedTo != null && (b.Stage == "won" || b.Stage == "lost"))
            .GroupBy(b => b.AssignedTo!.Value)
            .Select(g => new { UserId = g.Key, Won = g.Count(b => b.Stage == "won"), Total = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => new { x.Won, x.Total }, ct);

        // Checklist items completed by each user, org-wide.
        var tasks = await db.BidChecklistItems
            .Where(i => i.OrgId == orgId && i.IsDone && i.DoneBy != null)
            .GroupBy(i => i.DoneBy!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var result = new Dictionary<Guid, MemberStats>();
        foreach (var uid in active.Keys.Union(decided.Keys).Union(tasks.Keys))
        {
            decimal? winRate = null;
            if (decided.TryGetValue(uid, out var d) && d.Total > 0)
                winRate = Math.Round(d.Won * 100m / d.Total, 1);

            result[uid] = new MemberStats(
                active.GetValueOrDefault(uid),
                winRate,
                tasks.GetValueOrDefault(uid));
        }
        return result;
    }

    /// <summary>Read-guard: the caller must be an active member of the org (any role).</summary>
    private async Task RequireMemberAsync(Guid orgId, Guid userId, CancellationToken ct)
    {
        var isMember = await db.OrgMembers
            .AnyAsync(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active", ct);

        if (!isMember)
            throw new UnauthorizedAccessException("Not a member of this organization.");
    }

    private Task<Organization> LoadOrgAsync(Guid orgId, CancellationToken ct)
        => db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct)
            .ContinueWith(t => t.Result ?? throw new KeyNotFoundException("Organization not found."), ct);

    private async Task RequireRoleAsync(Guid orgId, Guid userId, string[] allowed, CancellationToken ct)
    {
        var role = await db.OrgMembers
            .Where(m => m.OrgId == orgId && m.UserId == userId && m.Status == "active")
            .Select(m => m.Role)
            .FirstOrDefaultAsync(ct);

        if (role is null || !allowed.Contains(role))
            throw new UnauthorizedAccessException("Insufficient permissions.");
    }

    private static OrgDetailDto MapToDetail(Organization org, string userRole, Dictionary<Guid, MemberStats> stats)
        => new(
            org.Id, org.Name, org.Slug, org.Gstin, org.Pan,
            org.Industry, org.CompanySize, org.RegisteredAddress,
            org.City, org.State, org.Pincode, org.Website,
            org.GemSellerId, org.PrimaryCategory, org.LogoUrl,
            org.IsActive, userRole, org.CreatedAt,
            org.Members.Select(m => MapMember(m, stats.GetValueOrDefault(m.UserId))).ToList());

    private static OrgMemberDto MapMember(OrgMember m, MemberStats stats = default) => new(
        m.Id, m.UserId,
        m.User?.Name ?? string.Empty,
        m.User?.Email ?? string.Empty,
        m.User?.AvatarUrl,
        m.Role, m.Department, m.Status,
        m.JoinedAt,
        stats.ActiveBids, stats.WinRate, stats.TasksDone);

    /// <summary>
    /// Fire-and-forget TEAM_INVITATION notification. Failures are logged but never
    /// surfaced — the calling branch's DB row (membership or pending invite) is
    /// already persisted by the time this runs.
    ///
    /// <para>Two callers:</para>
    /// <list type="bullet">
    ///   <item>Existing user → <paramref name="recipientUserId"/> is the user's id,
    ///         <paramref name="invitationLink"/> is a deep-link to the org page,
    ///         and the InApp channel goes to that user.</item>
    ///   <item>New invite → <paramref name="recipientUserId"/> is <c>null</c>,
    ///         <paramref name="invitationLink"/> carries a single-use registration
    ///         token, and only Email is sent (InApp has no inbox for an
    ///         unregistered recipient).</item>
    /// </list>
    /// </summary>
    private async Task SendInvitationAsync(
        Guid invitedBy,
        Guid? recipientUserId,
        string recipientEmail,
        string? recipientName,
        Guid orgId,
        string invitationLink,
        CancellationToken ct)
    {
        try
        {
            var orgName = await db.Organizations
                .Where(o => o.Id == orgId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            var inviterName = await db.Users
                .Where(u => u.Id == invitedBy)
                .Select(u => u.Name)
                .FirstOrDefaultAsync(ct) ?? string.Empty;

            // First name = recipientName's first token if we have it, else the local
            // part of their email (handlebars can't fix a missing key, so something
            // sensible-looking beats "{{FirstName}}").
            var firstName = !string.IsNullOrWhiteSpace(recipientName)
                ? FirstName(recipientName!)
                : recipientEmail.Split('@')[0];

            var recipients = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.Email, recipientEmail),
            };
            if (recipientUserId.HasValue)
            {
                // Only existing users have an in-app inbox to receive a notification.
                recipients.Add(new NotificationRecipientDto(NotificationChannel.InApp, recipientUserId.Value.ToString()));
            }

            await notifications.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "TEAM_INVITATION",
                UserId:       recipientUserId,
                Payload:      new Dictionary<string, object>
                {
                    ["FirstName"]        = firstName,
                    ["InvitedByName"]    = inviterName,
                    ["OrganizationName"] = orgName,
                    ["InvitationLink"]   = invitationLink,
                },
                Recipients: recipients), ct);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex,
                "TEAM_INVITATION notification failed for {Recipient} into org {OrgId}; the invite itself succeeded.",
                recipientEmail, orgId);
        }
    }

    private string BuildDeepLink(Guid orgId)
    {
        var frontendBase = (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        return $"{frontendBase}/orgs/{orgId}";
    }

    private string BuildRegisterLink(string rawToken, string email)
    {
        var frontendBase = (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        // Recipient lands on the SPA's signup page (note: SPA route is /signup,
        // not /auth/register — the API path is unrelated). SignupPage reads both
        // the `invite` and `email` query params, pre-fills + locks the email
        // field, and forwards the token as `inviteToken` to POST /api/auth/register.
        //
        // The email is NOT a secret — the recipient owns it and just received this
        // message at that address. The token IS the secret (it's bound to this
        // email server-side; mismatched email at register time → INVITE_INVALID).
        return $"{frontendBase}/signup" +
               $"?invite={Uri.EscapeDataString(rawToken)}" +
               $"&email={Uri.EscapeDataString(email)}";
    }

    /// <summary>
    /// Returns (rawToken, sha256-hex hash). The raw token is single-use and high
    /// entropy (32 random bytes, URL-safe base64). Only the hash is persisted;
    /// the raw token leaves the BFF exactly once — via the invite email.
    /// </summary>
    private static (string Raw, string Hash) GenerateInviteToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var raw = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');   // url-safe
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (raw, hash);
    }

    private static string FirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var space = fullName.IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName[..space].Trim();
    }
}
