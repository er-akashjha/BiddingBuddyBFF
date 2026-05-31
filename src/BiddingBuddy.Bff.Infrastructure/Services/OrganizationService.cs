using BiddingBuddy.Bff.Core.DTOs.Orgs;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class OrganizationService(BffDbContext db, IUserRepository userRepo) : IOrganizationService
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
        return MapToDetail(org, role);
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

        return members.Select(MapMember).ToList();
    }

    public async Task<OrgMemberDto> InviteMemberAsync(Guid orgId, Guid invitedBy, InviteMemberDto dto, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, invitedBy, ["owner", "admin"], ct);

        var user = await userRepo.FindByEmailAsync(dto.Email, ct)
            ?? throw new KeyNotFoundException($"No user with email '{dto.Email}'.");

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
        return MapMember(existing);
    }

    public async Task<OrgMemberDto> UpdateMemberAsync(Guid orgId, Guid memberId, Guid requestingUserId, UpdateMemberDto dto, CancellationToken ct = default)
    {
        await RequireRoleAsync(orgId, requestingUserId, ["owner", "admin"], ct);

        var member = await db.OrgMembers.Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Member not found.");

        if (dto.Role       is not null) member.Role       = dto.Role;
        if (dto.Department is not null) member.Department = dto.Department;
        if (dto.Status     is not null) member.Status     = dto.Status;

        await db.SaveChangesAsync(ct);
        return MapMember(member);
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

    // ── helpers ──────────────────────────────────────────────────────────────

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

    private static OrgDetailDto MapToDetail(Organization org, string userRole)
        => new(
            org.Id, org.Name, org.Slug, org.Gstin, org.Pan,
            org.Industry, org.CompanySize, org.RegisteredAddress,
            org.City, org.State, org.Pincode, org.Website,
            org.GemSellerId, org.PrimaryCategory, org.LogoUrl,
            org.IsActive, userRole, org.CreatedAt,
            org.Members.Select(MapMember).ToList());

    private static OrgMemberDto MapMember(OrgMember m) => new(
        m.Id, m.UserId,
        m.User?.Name ?? string.Empty,
        m.User?.Email ?? string.Empty,
        m.User?.AvatarUrl,
        m.Role, m.Department, m.Status,
        m.JoinedAt);
}
