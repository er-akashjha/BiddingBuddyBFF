using BiddingBuddy.Bff.Core.DTOs.Alerts;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Helpers;
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
    ITenderAlertRuleService alertRules,
    IConfiguration config,
    ILogger<OrganizationService> log) : IOrganizationService
{
    public async Task<OrgDetailDto> CreateAsync(Guid ownerId, CreateOrgDto dto, CancellationToken ct = default)
    {
        // Refuse to hand this user a second workspace for a company that already has one.
        // Before this check the insert was unconditional, so the second person from a company
        // to sign up silently became the owner of an empty parallel org — same legal entity,
        // disjoint bids/documents/compliance, neither side ever seeing the other.
        var duplicate = await FindDuplicateAsync(ownerId, dto, ct);
        if (duplicate is not null) throw new DuplicateOrganizationException(duplicate);

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
            GemSellerName     = dto.GemSellerName,
            PrimaryCategory   = dto.PrimaryCategory,
        };

        db.Organizations.Add(org);

        // Link through the navigation property — do NOT copy org.Id into OrgId here.
        //
        // organizations.id is store-generated (HasDefaultValueSql("gen_random_uuid()")), so
        // before SaveChanges org.Id is not a usable key. Assigning the raw scalar gave EF a
        // value with no relationship to the tracked principal, so the insert-ordering graph
        // had no edge between the two: EF emitted the org_members INSERT *before* the
        // organizations one, and Postgres rejected it with
        //   23503 org_members_org_id_fkey
        // which failed every single workspace creation.
        //
        // Setting the navigation makes the dependency explicit: EF inserts organizations
        // first and propagates the real generated id into org_members.org_id. Both rows still
        // go in one SaveChanges, so ownership stays atomic — an org can never be created
        // without its owner row.
        var ownerMember = new OrgMember
        {
            Organization = org,
            UserId       = ownerId,
            Role         = "owner",
            Status       = "active",
            JoinedAt     = DateTime.UtcNow,
        };
        db.OrgMembers.Add(ownerMember);

        await db.SaveChangesAsync(ct);

        await SeedStarterAlertRuleAsync(org.Id, ownerId, org.PrimaryCategory, ct);

        return await GetAsync(org.Id, ownerId, ct);
    }

    /// <summary>
    /// Does an active organization already represent this company? Returns the 409 payload
    /// if so, null to proceed.
    /// </summary>
    /// <remarks>
    /// Two signals with deliberately different strengths:
    /// <list type="bullet">
    /// <item><b>GSTIN — hard.</b> One GSTIN is one legal entity, so a match is conclusive and
    /// there is no override. Checked first, and checked even when the caller passes
    /// <c>AllowDuplicateName</c>: that flag is consent to a name coincidence, not to sharing
    /// a tax registration.</item>
    /// <item><b>Name — soft.</b> Unrelated firms genuinely share names, so this only warns;
    /// the client re-submits with <c>AllowDuplicateName</c> to proceed.</item>
    /// </list>
    /// Inactive orgs are ignored throughout — a deactivated workspace should not block its own
    /// company from starting again.
    /// </remarks>
    private async Task<OrgExistsDto?> FindDuplicateAsync(Guid userId, CreateOrgDto dto, CancellationToken ct)
    {
        var gstin = OrgIdentity.NormalizeGstin(dto.Gstin);
        if (gstin is not null)
        {
            // .Replace(" ", "").ToUpper() in THIS order renders as upper(replace(gstin,' ','')),
            // which is the expression migration 0030 indexes. Swapping the calls produces a
            // different expression and silently drops to a sequential scan.
            var byGstin = await db.Organizations
                .Where(o => o.IsActive && o.Gstin != null && o.Gstin.Replace(" ", "").ToUpper() == gstin)
                .Select(o => new { o.Id, o.Name, o.City })
                .FirstOrDefaultAsync(ct);

            if (byGstin is not null)
                return await BuildConflictAsync(userId, byGstin.Id, byGstin.Name, byGstin.City,
                                                match: "gstin", canOverride: false, ct);
        }

        if (dto.AllowDuplicateName) return null;

        var normalized = OrgIdentity.NormalizeName(dto.Name);
        var prefix = OrgIdentity.NamePrefix(dto.Name);
        if (prefix is null || normalized.Length == 0) return null;

        // Prefix in SQL, exact normalized comparison in C#. The database cannot run the
        // suffix-stripping rules, and pulling every org to apply them would not scale — so
        // narrow with something a btree can serve, then decide precisely on the candidates.
        // The cap bounds a pathological prefix (one letter, thousands of orgs); overflowing it
        // can only cause a missed warning, never a wrong block.
        var candidates = await db.Organizations
            .Where(o => o.IsActive && o.Name.ToLower().StartsWith(prefix))
            .OrderBy(o => o.CreatedAt)
            .Take(50)
            .Select(o => new { o.Id, o.Name, o.City })
            .ToListAsync(ct);

        var byName = candidates.FirstOrDefault(c => OrgIdentity.NormalizeName(c.Name) == normalized);
        if (byName is null) return null;

        return await BuildConflictAsync(userId, byName.Id, byName.Name, byName.City,
                                        match: "name", canOverride: true, ct);
    }

    /// <summary>Assembles the 409 body: the matched org, plus the caller's own live request
    /// against it so a re-run of onboarding shows "waiting for approval" rather than
    /// re-offering a button that would return the same row.</summary>
    private async Task<OrgExistsDto> BuildConflictAsync(
        Guid userId, Guid orgId, string orgName, string? city, string match, bool canOverride, CancellationToken ct)
    {
        var memberCount = await db.OrgMembers.CountAsync(m => m.OrgId == orgId && m.Status == "active", ct);

        var existing = await db.OrgJoinRequests
            .Where(r => r.OrgId == orgId && r.UserId == userId && r.Status == "pending")
            .Select(r => new MyJoinRequestDto(
                r.Id, r.OrgId, orgName, null, r.Status, r.Role, r.CreatedAt, r.DecidedAt))
            .FirstOrDefaultAsync(ct);

        return new OrgExistsDto(
            Error:       "ORG_EXISTS",
            Match:       match,
            CanOverride: canOverride,
            Org:         new DuplicateOrgSummaryDto(orgId, orgName, city, memberCount),
            ExistingRequest: existing);
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

        // Captured before the assignments below: onboarding mode 1 PATCHes the sector onto an org
        // that AuthService created without one, and that first set is the only one that seeds a
        // starter alert rule. Later sector changes are left alone — by then the org owns rules, and
        // silently rewriting a list the user curates in Settings → Interests would be worse than
        // doing nothing.
        var sectorWasUnset = string.IsNullOrWhiteSpace(org.PrimaryCategory);

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
        // The GeM identity pair is the one thing on this DTO a user needs to be able to UNSET:
        // it is free text on GeM's side, so a typo means "we never find you on a ladder", and the
        // fallback to the org name only kicks in while it is blank. null still means "not
        // supplied" for every field here, so blank — not null — is what clears these two.
        if (dto.GemSellerId   is not null) org.GemSellerId   = NullIfBlank(dto.GemSellerId);
        if (dto.GemSellerName is not null) org.GemSellerName = NullIfBlank(dto.GemSellerName);
        if (dto.PrimaryCategory is not null) org.PrimaryCategory = dto.PrimaryCategory;
        if (dto.LogoUrl    is not null) org.LogoUrl           = dto.LogoUrl;

        await db.SaveChangesAsync(ct);

        if (sectorWasUnset) await SeedStarterAlertRuleAsync(orgId, userId, org.PrimaryCategory, ct);

        return await GetAsync(orgId, userId, ct);
    }

    /// <summary>
    /// Turn the sector picked at onboarding into a real <c>tender_alert_rules</c> row, so the org
    /// starts matching tenders in that category instead of the sector being stored and ignored.
    /// </summary>
    /// <remarks>
    /// Two things make this work, and both are load-bearing:
    /// <list type="bullet">
    /// <item>The picker emits the canonical 40-entry taxonomy verbatim (ui v20), which is the same
    /// vocabulary the pipeline assigns to <c>tenders.category</c>. <c>MatchingService</c> compares
    /// categories with full-string <see cref="StringComparison.OrdinalIgnoreCase"/> — no substring,
    /// no stemming — so a free-form label like "IT &amp; Software" would match zero tenders forever
    /// and fail silently. Do not let a hand-typed sector reach this method.</item>
    /// <item>The org has no <c>org_alert_settings</c> row yet, so <c>last_digest_sent_at</c> is NULL
    /// and the cooldown gate does not trip: the first digest goes out on the next scan tick rather
    /// than 6 h later. That is deliberate — it is the "right away" the onboarding page promises —
    /// and it cannot flood, because the scan only ever evaluates tenders with
    /// <c>alerts_scanned_at IS NULL</c>. There is no backlog to blast, only newly-ingested tenders.</item>
    /// </list>
    /// </remarks>
    private async Task SeedStarterAlertRuleAsync(Guid orgId, Guid userId, string? primaryCategory, CancellationToken ct)
    {
        var category = NullIfBlank(primaryCategory);
        if (category is null) return;

        try
        {
            // Idempotency. The starter rule exists to bootstrap an org that has told us nothing
            // else; the moment the org owns ANY rule — this one from a re-run, or one the user
            // built by hand — it is no longer bootstrapping. `tender_alert_rules` carries no
            // unique constraint, so without this check a repeat call just silently appends a
            // duplicate the user has to find and delete.
            if (await db.TenderAlertRules.AnyAsync(r => r.OrgId == orgId, ct)) return;

            await alertRules.CreateAsync(orgId, userId, new CreateTenderAlertRuleDto(
                Name:       $"{category} tenders",
                Categories: [category],
                States:     null,
                Keywords:   null,
                MinValue:   null,
                MaxValue:   null,
                MinAiScore: null), ct);

            log.LogInformation(
                "Seeded starter tender alert rule for org {OrgId} from primary category {Category}.",
                orgId, category);
        }
        catch (Exception ex)
        {
            // Same contract as the notification publishes elsewhere in this class: a convenience
            // must never fail the parent flow. The org and the sector are already committed, and
            // the user can still build interests by hand in Settings → Interests.
            log.LogWarning(ex, "Could not seed starter tender alert rule for org {OrgId}.", orgId);
        }
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

        // An already-active member needs no invite — surface it instead of silently
        // re-adding (the old flow would overwrite their role without their consent).
        if (user is not null)
        {
            var existing = await db.OrgMembers
                .FirstOrDefaultAsync(m => m.OrgId == orgId && m.UserId == user.Id, ct);
            if (existing is not null && existing.Status == "active")
                throw new InvalidOperationException("ALREADY_MEMBER");
        }

        // Membership is never granted at invite time. Both cases below create a
        // pending invite; the invitee becomes a member only when they explicitly
        // confirm — existing users via the SPA accept page (POST /api/invites/accept),
        // new users by registering with the token.

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

        if (user is not null)
        {
            // Existing user → email + in-app link to the SPA accept page, where they
            // confirm (or decline) joining this org.
            var acceptLink = BuildAcceptLink(rawToken);
            await SendInvitationAsync(invitedBy, user.Id, user.Email, user.Name, orgId, acceptLink, ct);
        }
        else
        {
            // No account yet → email a registration link carrying the raw token. The
            // recipient registers via POST /api/auth/register with this token in the
            // body; AuthService validates + consumes the invite and joins them to this org.
            var registerLink = BuildRegisterLink(rawToken, email);
            await SendInvitationAsync(invitedBy, recipientUserId: null, email, recipientName: null,
                                      orgId, registerLink, ct);
        }

        return new InviteMemberResultDto(
            Status:       "invited",
            Member:       null,
            InvitedEmail: email,
            ExpiresAt:    expiresAt);
    }

    public async Task<InvitePreviewDto> GetInvitePreviewAsync(string token, CancellationToken ct = default)
    {
        var invite = await FindPendingInviteByTokenAsync(token, ct);

        var org = await db.Organizations
            .Where(o => o.Id == invite.OrgId)
            .Select(o => new { o.Name, o.LogoUrl })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("INVITE_INVALID");

        var inviterName = await db.Users
            .Where(u => u.Id == invite.InvitedBy)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var inviteeHasAccount = await userRepo.FindByEmailAsync(invite.Email, ct) is not null;

        return new InvitePreviewDto(
            org.Name, org.LogoUrl, inviterName,
            invite.Role, invite.Email, invite.ExpiresAt, inviteeHasAccount);
    }

    public async Task<AcceptInviteResultDto> AcceptInviteAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var invite = await FindPendingInviteByTokenAsync(token, ct);
        await RequireInviteeMatchAsync(invite, userId, ct);
        return await AcceptPendingInviteAsync(invite, userId, ct);
    }

    public async Task<IReadOnlyList<MyInviteDto>> GetMyPendingInvitesAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("User not found.");
        var email = user.Email.Trim().ToLowerInvariant();

        var now = DateTime.UtcNow;
        return await (
            from i in db.OrganizationInvites
            where i.Email.ToLower() == email && i.AcceptedAt == null && i.ExpiresAt > now
            join o in db.Organizations on i.OrgId equals o.Id
            join u in db.Users on i.InvitedBy equals u.Id into inviters
            from u in inviters.DefaultIfEmpty()
            orderby i.CreatedAt descending
            select new MyInviteDto(i.Id, o.Name, o.LogoUrl, u != null ? u.Name : string.Empty, i.Role, i.ExpiresAt)
        ).ToListAsync(ct);
    }

    public async Task<AcceptInviteResultDto> AcceptInviteByIdAsync(Guid userId, Guid inviteId, CancellationToken ct = default)
    {
        var invite = await db.OrganizationInvites.FirstOrDefaultAsync(i => i.Id == inviteId, ct);
        if (invite is null || invite.AcceptedAt is not null || invite.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("INVITE_INVALID");

        await RequireInviteeMatchAsync(invite, userId, ct);
        return await AcceptPendingInviteAsync(invite, userId, ct);
    }

    /// <summary>Shared accept body: create/reactivate the membership and consume the invite.
    /// Callers have already resolved a live invite and verified the invitee email matches.</summary>
    private async Task<AcceptInviteResultDto> AcceptPendingInviteAsync(OrganizationInvite invite, Guid userId, CancellationToken ct)
    {
        var member = await db.OrgMembers
            .FirstOrDefaultAsync(m => m.OrgId == invite.OrgId && m.UserId == userId, ct);

        if (member is null)
        {
            db.OrgMembers.Add(new OrgMember
            {
                OrgId      = invite.OrgId,
                UserId     = userId,
                Role       = invite.Role,
                Department = invite.Department,
                Status     = "active",
                InvitedBy  = invite.InvitedBy,
                JoinedAt   = DateTime.UtcNow,
                CreatedAt  = DateTime.UtcNow,
            });
        }
        else
        {
            // e.g. a previously suspended member re-invited — reactivate with the invite's role.
            member.Status     = "active";
            member.Role       = invite.Role;
            member.Department = invite.Department;
            member.JoinedAt   = DateTime.UtcNow;
        }

        invite.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var orgName = await db.Organizations
            .Where(o => o.Id == invite.OrgId)
            .Select(o => o.Name)
            .FirstAsync(ct);

        return new AcceptInviteResultDto(invite.OrgId, orgName, invite.Role);
    }

    public async Task DeclineInviteAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var invite = await FindPendingInviteByTokenAsync(token, ct);
        await RequireInviteeMatchAsync(invite, userId, ct);

        // The schema has no separate declined state — consuming the token (accepted_at)
        // is what matters: it can't be redeemed and drops off the org's pending list.
        invite.AcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Token → live invite row, or <c>INVITE_INVALID</c> for unknown/consumed/expired.</summary>
    private async Task<OrganizationInvite> FindPendingInviteByTokenAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("INVITE_INVALID");

        var hash = HashRawToken(token);
        var invite = await db.OrganizationInvites
            .FirstOrDefaultAsync(i => i.TokenHash == hash, ct);

        if (invite is null || invite.AcceptedAt is not null || invite.ExpiresAt < DateTime.UtcNow)
            throw new InvalidOperationException("INVITE_INVALID");

        return invite;
    }

    /// <summary>The token alone isn't enough to join — the logged-in caller must own the
    /// invited email, otherwise a forwarded link would let anyone into the org.</summary>
    private async Task RequireInviteeMatchAsync(OrganizationInvite invite, Guid userId, CancellationToken ct)
    {
        var user = await userRepo.FindByIdAsync(userId, ct)
            ?? throw new KeyNotFoundException("User not found.");
        if (!string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("INVITE_EMAIL_MISMATCH");
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
            org.GemSellerId, org.GemSellerName, org.PrimaryCategory, org.LogoUrl,
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

    /// <summary>Link for invitees who already have an account — the SPA's accept page,
    /// where they sign in (if needed) and explicitly confirm or decline joining.</summary>
    private string BuildAcceptLink(string rawToken)
    {
        var frontendBase = (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');
        return $"{frontendBase}/invites/accept?token={Uri.EscapeDataString(rawToken)}";
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
        return (raw, HashRawToken(raw));
    }

    private static string HashRawToken(string raw)
    {
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string FirstName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var space = fullName.IndexOf(' ');
        return space < 0 ? fullName.Trim() : fullName[..space].Trim();
    }

    /// <summary>Blank → null, so a cleared field is stored as absent rather than as "".</summary>
    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
