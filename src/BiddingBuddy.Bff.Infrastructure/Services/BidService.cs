using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class BidService(BffDbContext db, IBiddingBuddyServicesClient servicesClient) : IBidService
{
    public async Task<PagedResult<BidListItemDto>> ListAsync(
        Guid orgId, string? stage, string? priority, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Bids.Where(b => b.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(stage))    query = query.Where(b => b.Stage == stage);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(b => b.Priority == priority);

        var total = await query.CountAsync(ct);
        var pg    = Math.Max(1, page);
        var sz    = Math.Clamp(pageSize, 1, 100);

        var bids = await query
            .Include(b => b.AssignedUser)
            .Include(b => b.Tender)
            .OrderByDescending(b => b.UpdatedAt)
            .Skip((pg - 1) * sz)
            .Take(sz)
            .ToListAsync(ct);

        var items = bids.Select(b => new BidListItemDto(
            b.Id, b.Title, b.Stage, b.Priority, b.DueDate,
            b.TenderValue, b.OurBidValue, b.WinProbability, b.ProgressPct,
            b.AssignedTo, b.AssignedUser?.Name,
            b.TenderId, b.Tender?.GemTenderId, b.UpdatedAt)).ToList();

        return new PagedResult<BidListItemDto>(items, total, pg, sz);
    }

    public async Task<BidDetailDto> GetAsync(Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);
        return await MapToDetailAsync(bid, ct);
    }

    public async Task<BidDetailDto> CreateAsync(Guid orgId, Guid userId, CreateBidDto dto, CancellationToken ct = default)
    {
        // The bids.tender_id FK references the local PostgreSQL tenders table, but the
        // canonical tender data lives in MongoDB (BiddingBuddyServices). If the user
        // picked a tender that hasn't been mirrored locally yet, fetch + upsert a stub
        // so the FK resolves.
        if (dto.TenderId.HasValue)
        {
            await EnsureLocalTenderAsync(dto.TenderId.Value, ct);
        }

        var bid = new Bid
        {
            Id            = Guid.NewGuid(),
            OrgId         = orgId,
            TenderId      = dto.TenderId,
            Title         = dto.Title,
            Description   = dto.Description,
            Stage         = dto.Stage,
            Priority      = dto.Priority,
            AssignedTo    = dto.AssignedTo,
            DueDate       = dto.DueDate,
            TenderValue   = dto.TenderValue,
            OurBidValue   = dto.OurBidValue,
            WinProbability = dto.WinProbability,
            CreatedBy     = userId,
        };
        db.Bids.Add(bid);

        db.BidActivities.Add(new BidActivity
        {
            BidId   = bid.Id,
            ActorId = userId,
            Action  = "created",
        });

        await db.SaveChangesAsync(ct);
        return await GetAsync(bid.Id, orgId, ct);
    }

    public async Task<BidDetailDto> UpdateAsync(Guid bidId, Guid orgId, UpdateBidDto dto, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);

        if (dto.Title          is not null) bid.Title          = dto.Title;
        if (dto.Description    is not null) bid.Description    = dto.Description;
        if (dto.Stage          is not null) bid.Stage          = dto.Stage;
        if (dto.Priority       is not null) bid.Priority       = dto.Priority;
        if (dto.AssignedTo.HasValue)        bid.AssignedTo     = dto.AssignedTo;
        if (dto.DueDate.HasValue)           bid.DueDate        = dto.DueDate;
        if (dto.TenderValue.HasValue)       bid.TenderValue    = dto.TenderValue;
        if (dto.OurBidValue.HasValue)       bid.OurBidValue    = dto.OurBidValue;
        if (dto.WinProbability.HasValue)    bid.WinProbability = dto.WinProbability;
        if (dto.ProgressPct.HasValue)       bid.ProgressPct    = dto.ProgressPct.Value;
        if (dto.LossReason     is not null) bid.LossReason     = dto.LossReason;
        if (dto.WonValue.HasValue)          bid.WonValue       = dto.WonValue;

        await db.SaveChangesAsync(ct);
        return await GetAsync(bidId, orgId, ct);
    }

    public async Task<BidDetailDto> ChangeStageAsync(Guid bidId, Guid orgId, Guid actorId, ChangeStageDto dto, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);
        var fromStage = bid.Stage;

        bid.Stage = dto.Stage;
        db.BidActivities.Add(new BidActivity
        {
            BidId     = bid.Id,
            ActorId   = actorId,
            Action    = "stage_change",
            FromValue = fromStage,
            ToValue   = dto.Stage,
            Note      = dto.Note,
        });

        await db.SaveChangesAsync(ct);
        return await GetAsync(bidId, orgId, ct);
    }

    public async Task DeleteAsync(Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);
        db.Bids.Remove(bid);
        await db.SaveChangesAsync(ct);
    }

    // ── Activities ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BidActivityDto>> GetActivitiesAsync(Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        return await db.BidActivities
            .Include(a => a.Actor)
            .Where(a => a.BidId == bidId)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new BidActivityDto(
                a.Id, a.ActorId, a.Actor.Name,
                a.Action, a.FromValue, a.ToValue, a.Note, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<BidActivityDto> AddNoteAsync(Guid bidId, Guid orgId, Guid actorId, AddNoteDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var activity = new BidActivity
        {
            BidId   = bidId,
            ActorId = actorId,
            Action  = "note_added",
            Note    = dto.Note,
        };
        db.BidActivities.Add(activity);
        await db.SaveChangesAsync(ct);

        var actor = await db.Users.FindAsync([actorId], ct);
        return new BidActivityDto(
            activity.Id, actorId, actor?.Name ?? string.Empty,
            "note_added", null, null, dto.Note, activity.CreatedAt);
    }

    // ── Comments ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BidCommentDto>> GetCommentsAsync(Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        return await db.BidComments
            .Include(c => c.Author)
            .Where(c => c.BidId == bidId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new BidCommentDto(
                c.Id, c.BidId, c.AuthorId, c.Author.Name,
                c.Body, c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<BidCommentDto> AddCommentAsync(Guid bidId, Guid orgId, Guid authorId, AddCommentDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        if (string.IsNullOrWhiteSpace(dto.Body))
            throw new ArgumentException("Comment body cannot be empty.", nameof(dto));

        var comment = new BidComment
        {
            Id       = Guid.NewGuid(),
            BidId    = bidId,
            AuthorId = authorId,
            Body     = dto.Body.Trim(),
        };
        db.BidComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var authorName = await GetUserNameAsync(authorId, ct) ?? string.Empty;
        return new BidCommentDto(
            comment.Id, comment.BidId, comment.AuthorId, authorName,
            comment.Body, comment.CreatedAt, comment.UpdatedAt);
    }

    // ── Checklist ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ChecklistItemDto>> GetChecklistAsync(Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        return await db.BidChecklistItems
            .Where(i => i.BidId == bidId)
            .OrderBy(i => i.SortOrder)
            .Select(i => new ChecklistItemDto(
                i.Id, i.Title, i.IsDone, i.DueDate, i.AssignedTo,
                i.AssignedTo == null
                    ? null
                    : db.Users.Where(u => u.Id == i.AssignedTo).Select(u => u.Name).FirstOrDefault(),
                i.DoneAt, i.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<ChecklistItemDto> CreateChecklistItemAsync(Guid bidId, Guid orgId, CreateChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var item = new BidChecklistItem
        {
            BidId      = bidId,
            OrgId      = orgId,
            Title      = dto.Title,
            DueDate    = dto.DueDate,
            AssignedTo = dto.AssignedTo,
            SortOrder  = dto.SortOrder,
        };
        db.BidChecklistItems.Add(item);
        await db.SaveChangesAsync(ct);

        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task<ChecklistItemDto> UpdateChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, Guid actorId, UpdateChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var item = await db.BidChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.BidId == bidId, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");

        if (dto.Title       is not null) item.Title      = dto.Title;
        if (dto.DueDate.HasValue)        item.DueDate    = dto.DueDate;
        if (dto.AssignedTo.HasValue)     item.AssignedTo = dto.AssignedTo;
        if (dto.SortOrder.HasValue)      item.SortOrder  = dto.SortOrder.Value;

        if (dto.IsDone.HasValue)
        {
            item.IsDone = dto.IsDone.Value;
            item.DoneAt = dto.IsDone.Value ? DateTime.UtcNow : null;
            item.DoneBy = dto.IsDone.Value ? actorId : null;
        }

        await db.SaveChangesAsync(ct);
        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task DeleteChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var item = await db.BidChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.BidId == bidId, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");

        db.BidChecklistItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures a row exists in the local PostgreSQL <c>tenders</c> table for the given id.
    /// The canonical tender lives in MongoDB via BiddingBuddyServices; we mirror a minimal
    /// stub here so that the <c>bids.tender_id</c> foreign key resolves.
    /// </summary>
    private async Task EnsureLocalTenderAsync(Guid tenderId, CancellationToken ct)
    {
        var exists = await db.Tenders.AnyAsync(t => t.Id == tenderId, ct);
        if (exists) return;

        TenderDetailDto remote;
        try
        {
            remote = await servicesClient.GetTenderAsync(tenderId.ToString(), ct);
        }
        catch (Exception ex)
        {
            throw new KeyNotFoundException(
                $"Tender {tenderId} not found in BiddingBuddyServices: {ex.Message}", ex);
        }

        var now = DateTime.UtcNow;
        var stub = new Tender
        {
            Id               = tenderId,
            GemTenderId      = string.IsNullOrWhiteSpace(remote.GemTenderId) ? tenderId.ToString() : remote.GemTenderId,
            Title            = string.IsNullOrWhiteSpace(remote.Title) ? "(untitled)" : remote.Title,
            Description      = remote.Description,
            BuyerOrgName     = remote.BuyerOrgName,
            BuyerOrgIdGem    = remote.BuyerOrgIdGem,
            State            = remote.State,
            City             = remote.City,
            Category         = remote.Category,
            SubCategory      = remote.SubCategory,
            TenderValue      = remote.TenderValue,
            EmdAmount        = remote.EmdAmount,
            PublishedDate    = remote.PublishedDate,
            ClosingDate      = remote.ClosingDate,
            DeliveryDays     = remote.DeliveryDays,
            Status           = MapTenderStatus(remote.Status),
            CorrigendumCount = remote.CorrigendumCount,
            AiScore          = remote.AiScore,
            EligibilityScore = remote.EligibilityScore,
            WinProbability   = remote.WinProbability,
            RiskScore        = remote.RiskScore,
            AiSummary        = remote.AiSummary,
            AiTags           = remote.AiTags,
            Source           = "gem_pipeline",
            CreatedAt        = now,
            UpdatedAt        = now,
        };

        // Tender.Id is configured with HasDefaultValueSql("gen_random_uuid()") in
        // TenderConfiguration, so EF marks it ValueGenerated.OnAdd. Force EF to use
        // our explicit id (which must match dto.TenderId) instead of letting Postgres
        // generate a new one — otherwise the inserted tender row would have a
        // different id than the FK the bid is pointing at.
        var entry = db.Tenders.Add(stub);
        entry.Property(x => x.Id).IsTemporary = false;

        // Commit the tender stub immediately so the bid insert in the caller's
        // SaveChangesAsync sees it for the FK check.
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Maps a BiddingBuddyServices (MongoDB) tender status to the values allowed by the
    /// PostgreSQL <c>tenders_status_check</c> constraint: active | closed | cancelled | awarded.
    /// Mongo uses values like "open" which would otherwise violate the constraint.
    /// </summary>
    private static string MapTenderStatus(string? remoteStatus) =>
        (remoteStatus ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "open" or "active" or "live" or "published" or "" => "active",
            "closed" or "expired"                             => "closed",
            "cancelled" or "canceled" or "withdrawn"          => "cancelled",
            "awarded" or "completed"                          => "awarded",
            _                                                 => "active",
        };

    private async Task<Bid> LoadBidAsync(Guid bidId, Guid orgId, CancellationToken ct)
    {
        var bid = await db.Bids
            .Include(b => b.AssignedUser)
            .Include(b => b.Tender)
            .FirstOrDefaultAsync(b => b.Id == bidId && b.OrgId == orgId, ct);

        return bid ?? throw new KeyNotFoundException("Bid not found.");
    }

    private async Task EnsureBidBelongsAsync(Guid bidId, Guid orgId, CancellationToken ct)
    {
        var exists = await db.Bids.AnyAsync(b => b.Id == bidId && b.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Bid not found.");
    }

    private async Task<BidDetailDto> MapToDetailAsync(Bid bid, CancellationToken ct)
    {
        var recentActivities = await db.BidActivities
            .Include(a => a.Actor)
            .Where(a => a.BidId == bid.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new BidActivityDto(
                a.Id, a.ActorId, a.Actor.Name,
                a.Action, a.FromValue, a.ToValue, a.Note, a.CreatedAt))
            .ToListAsync(ct);

        var checklistTotal = await db.BidChecklistItems.CountAsync(i => i.BidId == bid.Id, ct);
        var checklistDone  = await db.BidChecklistItems.CountAsync(i => i.BidId == bid.Id && i.IsDone, ct);

        return new BidDetailDto(
            bid.Id, bid.OrgId, bid.TenderId,
            bid.Tender?.GemTenderId, bid.Title, bid.Description,
            bid.Stage, bid.Priority,
            bid.AssignedTo, bid.AssignedUser?.Name,
            bid.DueDate, bid.TenderValue, bid.OurBidValue,
            bid.WinProbability, bid.ProgressPct,
            bid.LossReason, bid.WonValue,
            bid.CreatedAt, bid.UpdatedAt,
            recentActivities, checklistTotal, checklistDone);
    }

    private static ChecklistItemDto MapChecklist(BidChecklistItem i, string? assignedName)
        => new(i.Id, i.Title, i.IsDone, i.DueDate, i.AssignedTo, assignedName, i.DoneAt, i.SortOrder);

    private async Task<string?> GetUserNameAsync(Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return null;
        return await db.Users
            .Where(u => u.Id == userId.Value)
            .Select(u => u.Name)
            .FirstOrDefaultAsync(ct);
    }
}
