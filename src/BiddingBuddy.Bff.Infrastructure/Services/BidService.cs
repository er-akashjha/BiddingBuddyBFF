using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Bids;
using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.DTOs.Tenders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class BidService(
    BffDbContext db,
    IBiddingBuddyServicesClient servicesClient,
    INotificationPublisher notificationPublisher,
    INotificationAudienceResolver audience,
    IConfiguration config,
    ILogger<BidService> logger) : IBidService
{
    // Roles told about won/lost outcomes (alongside the bid's own assignee).
    private static readonly string[] BidNotifyRoles = ["owner", "admin", "bid_manager"];

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<PagedResult<BidListItemDto>> ListAsync(
        Guid orgId, Guid currentUserId, BidListQuery q, CancellationToken ct = default)
    {
        var query = db.Bids.Where(b => b.OrgId == orgId);

        if (!string.IsNullOrWhiteSpace(q.Stage))    query = query.Where(b => b.Stage == q.Stage);
        if (!string.IsNullOrWhiteSpace(q.Priority)) query = query.Where(b => b.Priority == q.Priority);

        if (!string.IsNullOrWhiteSpace(q.StatusCategory))
        {
            if (!BidStages.IsValidStatusCategory(q.StatusCategory))
                throw new ArgumentException($"Invalid statusCategory '{q.StatusCategory}'. Allowed: open, closed.", nameof(q));
            query = query.Where(b => b.StatusCategory == q.StatusCategory);
        }

        if (q.AssignedTo.HasValue) query = query.Where(b => b.AssignedTo == q.AssignedTo);
        if (q.DueBefore.HasValue)  query = query.Where(b => b.DueDate != null && b.DueDate <= q.DueBefore);

        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var like = $"%{q.Q.Trim()}%";
            query = query.Where(b =>
                EF.Functions.ILike(b.Title, like) ||
                (b.Tender != null && b.Tender.GemTenderId != null && EF.Functions.ILike(b.Tender.GemTenderId, like)));
        }

        var total = await query.CountAsync(ct);
        var pg    = Math.Max(1, q.Page);
        var sz    = Math.Clamp(q.PageSize, 1, 100);

        var bids = await ApplySort(query, q.Sort)
            .Include(b => b.AssignedUser)
            .Include(b => b.Tender)
            .Skip((pg - 1) * sz)
            .Take(sz)
            .ToListAsync(ct);

        var summaries = await TaskSummariesAsync(bids.Select(b => b.Id).ToList(), currentUserId, ct);

        var items = bids.Select(b => new BidListItemDto(
            b.Id, b.Title, b.Stage, b.StatusCategory, b.Priority, b.DueDate,
            b.TenderValue, b.OurBidValue, b.WinProbability, b.ProgressPct,
            b.AssignedTo, b.AssignedUser?.Name,
            b.TenderId, b.Tender?.GemTenderId, b.UpdatedAt,
            summaries.GetValueOrDefault(b.Id, new TaskSummaryDto(0, 0, 0, 0)))).ToList();

        return new PagedResult<BidListItemDto>(items, total, pg, sz);
    }

    /// <summary>
    /// Per-bid checklist roll-up for the given bid ids, in one grouped query (no N+1).
    /// <paramref name="currentUserId"/> drives the <c>MineOpen</c> count.
    /// </summary>
    private async Task<Dictionary<Guid, TaskSummaryDto>> TaskSummariesAsync(
        IReadOnlyCollection<Guid> bidIds, Guid currentUserId, CancellationToken ct)
    {
        if (bidIds.Count == 0) return [];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var rows = await db.BidChecklistItems
            .Where(i => bidIds.Contains(i.BidId))
            .GroupBy(i => i.BidId)
            .Select(g => new
            {
                BidId    = g.Key,
                Total    = g.Count(),
                Done     = g.Count(i => i.IsDone),
                Overdue  = g.Count(i => !i.IsDone && i.DueDate != null && i.DueDate < today),
                MineOpen = g.Count(i => !i.IsDone && i.AssignedTo == currentUserId),
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.BidId, r => new TaskSummaryDto(r.Total, r.Done, r.Overdue, r.MineOpen));
    }

    public async Task<IReadOnlyList<MyTaskDto>> GetMyTasksAsync(
        Guid orgId, Guid userId, string? status, CancellationToken ct = default)
    {
        var query =
            from i in db.BidChecklistItems
            join b in db.Bids on i.BidId equals b.Id
            where i.OrgId == orgId && i.AssignedTo == userId
            select new { i.Id, i.BidId, BidTitle = b.Title, b.Stage, ItemTitle = i.Title, i.DueDate, i.IsDone };

        if (string.Equals(status, "open", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => !x.IsDone);
        else if (string.Equals(status, "done", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.IsDone);

        var rows = await query.ToListAsync(ct);

        return rows
            .Select(x => new MyTaskDto(
                x.Id, x.BidId, x.BidTitle, x.Stage, x.ItemTitle, x.DueDate, x.IsDone,
                TaskBucket(x.DueDate, x.IsDone)))
            // overdue first, then soonest due; undated last; done at the very end
            .OrderBy(t => t.IsDone)
            .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            .ToList();
    }

    /// <summary>Buckets a task by due date for the "My work" view.</summary>
    private static string TaskBucket(DateOnly? due, bool done)
    {
        if (done) return "done";
        if (due is null) return "later";
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (due < today) return "overdue";
        if (due == today) return "today";
        if (due <= today.AddDays(7)) return "week";
        return "later";
    }

    public async Task<IReadOnlyList<BidByTenderDto>> GetByTenderIdsAsync(
        Guid orgId, IReadOnlyCollection<Guid> tenderIds, CancellationToken ct = default)
    {
        if (tenderIds is null || tenderIds.Count == 0)
            return [];

        // Distinct + capped: this backs a single tender-list page (≤100 rows), so bound the
        // IN (...) list defensively rather than trusting the caller.
        var ids = tenderIds.Distinct().Take(100).ToList();
        var idStrings = ids.Select(i => i.ToString()).ToList();

        // The UI asks by the tender's MongoDB _id (the tender list/detail proxy Mongo), but
        // bids.tender_id points at the LOCAL Postgres tender PK, which the pipeline generates
        // independently of the Mongo id (see EnsureLocalTenderAsync). So a bid for a
        // pipeline-mirrored tender has tender_id = the random Postgres PK, NOT the Mongo id the
        // caller passed — matching only on tender_id would silently miss it and the
        // "already in pipeline" badge would never light up (its create path, however, stamps
        // the tender row's mongo_tender_id). Match EITHER the local PK directly OR the linked
        // tender row's mongo_tender_id, then key each result back to the id the caller queried
        // with so the frontend's lookup map (keyed by Mongo id) resolves.
        var bids = await db.Bids
            .Where(b => b.OrgId == orgId && b.TenderId != null)
            .Where(b => ids.Contains(b.TenderId!.Value)
                     || (b.Tender != null && b.Tender.MongoTenderId != null
                         && idStrings.Contains(b.Tender.MongoTenderId)))
            .Include(b => b.AssignedUser)
            .Include(b => b.Tender)
            .OrderByDescending(b => b.UpdatedAt)
            .ToListAsync(ct);

        // One entry per requested tender — the most recently updated bid wins when several exist.
        var result = new List<BidByTenderDto>(bids.Count);
        var seen = new HashSet<Guid>();
        foreach (var b in bids) // already newest-first
        {
            // Prefer the direct PK match; otherwise resolve back to the Mongo id the caller asked for.
            var key = ids.Contains(b.TenderId!.Value)
                ? b.TenderId!.Value
                : (Guid.TryParse(b.Tender?.MongoTenderId, out var mongoKey) ? mongoKey : b.TenderId!.Value);
            if (!seen.Add(key)) continue;
            result.Add(new BidByTenderDto(key, b.Id, b.AssignedTo, b.AssignedUser?.Name, b.Stage));
        }
        return result;
    }

    /// <summary>
    /// Whitelisted ordering. <paramref name="sort"/> is a field name with an optional
    /// leading '-' for descending. Unknown fields are rejected (400) rather than silently
    /// ignored. Default is most-recently-updated first.
    /// </summary>
    private static IQueryable<Bid> ApplySort(IQueryable<Bid> query, string? sort)
    {
        var key = sort?.Trim();
        if (string.IsNullOrEmpty(key))
            return query.OrderByDescending(b => b.UpdatedAt); // default: most recently updated first

        var desc = key[0] == '-';
        if (desc) key = key[1..];

        return key switch
        {
            "updatedAt" => desc ? query.OrderByDescending(b => b.UpdatedAt) : query.OrderBy(b => b.UpdatedAt),
            "dueDate"   => desc ? query.OrderByDescending(b => b.DueDate)   : query.OrderBy(b => b.DueDate),
            "priority"  => desc ? query.OrderByDescending(b => b.Priority)  : query.OrderBy(b => b.Priority),
            "title"     => desc ? query.OrderByDescending(b => b.Title)     : query.OrderBy(b => b.Title),
            _ => throw new ArgumentException($"Invalid sort '{sort}'. Allowed: updatedAt, dueDate, priority, title (optionally prefixed with '-').", nameof(sort)),
        };
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
        if (!BidStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'. Allowed: {string.Join(", ", BidStages.All)}.", nameof(dto));

        // The local tender row may already exist under a different primary key than the
        // id the UI sent (the pipeline mirrors tenders with a Postgres-generated id, while
        // the UI sends the Mongo _id). EnsureLocalTenderAsync resolves/creates the row and
        // returns the id the bid's FK must actually point at.
        Guid? localTenderId = dto.TenderId;
        if (dto.TenderId.HasValue)
        {
            localTenderId = await EnsureLocalTenderAsync(dto.TenderId.Value, ct);
        }

        // "Add to bid pipeline" is one-bid-per-tender by design. If a bid already exists for this
        // tender in the org, return it instead of creating a duplicate — this makes the action
        // idempotent and stops the tender list + detail pages (which both expose the button) from
        // racing in a third duplicate before the "already in pipeline" badge has loaded.
        if (localTenderId.HasValue)
        {
            var existingBid = await db.Bids
                .Where(b => b.OrgId == orgId && b.TenderId == localTenderId.Value)
                .OrderByDescending(b => b.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (existingBid is not null)
                return await GetAsync(existingBid.Id, orgId, ct);
        }

        var bid = new Bid
        {
            Id            = Guid.NewGuid(),
            OrgId         = orgId,
            TenderId      = localTenderId,
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

        // Assigning at create time ("Add to pipeline → assign to teammate") is just as much a
        // "bid assigned" event as a later reassignment, so fire the same notification. UpdateAsync
        // covers the reassign path; without this, the most common assignment flow sent nothing.
        if (bid.AssignedTo.HasValue)
            await NotifyBidAssignedAsync(bid, userId, ct);

        return await GetAsync(bid.Id, orgId, ct);
    }

    public async Task<BidDetailDto> UpdateAsync(Guid bidId, Guid orgId, Guid actorId, UpdateBidDto dto, CancellationToken ct = default)
    {
        var bid = await LoadBidAsync(bidId, orgId, ct);
        var previousAssignee = bid.AssignedTo;

        if (dto.Stage is not null && !BidStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'. Allowed: {string.Join(", ", BidStages.All)}.", nameof(dto));

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

        // Reassignment is the canonical "bid assigned" trigger (PATCH /api/bids backs both the
        // bid editor and the tender-detail "reassign owner" action). Stage-driven notifications
        // live on ChangeStageAsync — the dedicated stage endpoint — to avoid double-firing.
        if (bid.AssignedTo != previousAssignee)
            await NotifyBidAssignedAsync(bid, actorId, ct);

        return await GetAsync(bidId, orgId, ct);
    }

    public async Task<BidDetailDto> ChangeStageAsync(Guid bidId, Guid orgId, Guid actorId, ChangeStageDto dto, CancellationToken ct = default)
    {
        if (!BidStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'. Allowed: {string.Join(", ", BidStages.All)}.", nameof(dto));

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

        await NotifyStageChangedAsync(bid, fromStage, dto.Stage, actorId, ct);

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

        var comments = await db.BidComments
            .Include(c => c.Author)
            .Where(c => c.BidId == bidId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        var commentIds = comments.Select(c => c.Id).ToList();
        var attachments = await db.BidAttachments
            .Include(a => a.Uploader)
            .Where(a => a.CommentId != null && commentIds.Contains(a.CommentId!.Value))
            .ToListAsync(ct);
        var attByComment = attachments
            .GroupBy(a => a.CommentId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<BidAttachmentDto>)g.Select(MapAttachment).ToList());

        var itemIds = comments.Where(c => c.ChecklistItemId != null).Select(c => c.ChecklistItemId!.Value).Distinct().ToList();
        var itemTitles = await db.BidChecklistItems
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Title, ct);

        return comments.Select(c => new BidCommentDto(
            c.Id, c.BidId, c.AuthorId, c.Author.Name,
            c.Body, c.Kind, c.ChecklistItemId,
            c.ChecklistItemId is Guid id ? itemTitles.GetValueOrDefault(id) : null,
            attByComment.GetValueOrDefault(c.Id, []),
            c.CreatedAt, c.UpdatedAt)).ToList();
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

        var mentioned = (dto.MentionedUserIds ?? [])
            .Where(uid => uid != authorId)
            .Distinct()
            .ToList();
        await NotifyBidCommentAsync(bidId, orgId, authorId, authorName, comment.Body, mentioned, ct);

        return new BidCommentDto(
            comment.Id, comment.BidId, comment.AuthorId, authorName,
            comment.Body, comment.Kind, comment.ChecklistItemId, null, [],
            comment.CreatedAt, comment.UpdatedAt);
    }

    private static BidAttachmentDto MapAttachment(BidAttachment a)
        => new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedBy, a.Uploader?.Name, a.CreatedAt);

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

    public async Task<ChecklistItemDto> CreateChecklistItemAsync(Guid bidId, Guid orgId, Guid actorId, CreateChecklistItemDto dto, CancellationToken ct = default)
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

        await NotifyTaskAssignedAsync(item, actorId, ct);
        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task<ChecklistItemDto> UpdateChecklistItemAsync(Guid itemId, Guid bidId, Guid orgId, Guid actorId, UpdateChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        var item = await db.BidChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.BidId == bidId, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");

        var previousAssignee = item.AssignedTo;

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

        // Notify only when this update reassigned the task to a *different* person.
        if (item.AssignedTo != previousAssignee)
            await NotifyTaskAssignedAsync(item, actorId, ct);

        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task<CompleteChecklistResultDto> CompleteChecklistItemAsync(
        Guid itemId, Guid bidId, Guid orgId, Guid actorId, CompleteChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBidBelongsAsync(bidId, orgId, ct);

        if (string.IsNullOrWhiteSpace(dto.Note))
            throw new ArgumentException("A note is required to close a task.", nameof(dto));

        var item = await db.BidChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.BidId == bidId, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");

        item.IsDone = true;
        item.DoneAt = DateTime.UtcNow;
        item.DoneBy = actorId;

        var comment = new BidComment
        {
            Id              = Guid.NewGuid(),
            BidId           = bidId,
            AuthorId        = actorId,
            Body            = dto.Note.Trim(),
            ChecklistItemId = itemId,
            Kind            = "task_completion",
        };
        db.BidComments.Add(comment);

        // Link an optional, previously-registered attachment to this completion note.
        if (dto.AttachmentId is Guid attId)
        {
            var att = await db.BidAttachments
                .FirstOrDefaultAsync(a => a.Id == attId && a.BidId == bidId && a.OrgId == orgId, ct)
                ?? throw new KeyNotFoundException("Attachment not found.");
            att.CommentId = comment.Id;
            att.ChecklistItemId = itemId;
        }

        await db.SaveChangesAsync(ct); // item + note (+ attachment link) in one transaction

        var itemDto = MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));

        var authorName = await GetUserNameAsync(actorId, ct) ?? string.Empty;
        var attachments = await db.BidAttachments
            .Include(a => a.Uploader)
            .Where(a => a.CommentId == comment.Id)
            .ToListAsync(ct);
        var noteDto = new BidCommentDto(
            comment.Id, comment.BidId, comment.AuthorId, authorName,
            comment.Body, comment.Kind, comment.ChecklistItemId, item.Title,
            attachments.Select(MapAttachment).ToList(),
            comment.CreatedAt, comment.UpdatedAt);

        return new CompleteChecklistResultDto(itemDto, noteDto);
    }

    // ── Task-assigned notification (BID-301) ───────────────────────────────────

    /// <summary>
    /// Fire a TASK_ASSIGNED notification (InApp always; Email unless the assignee opted out).
    /// No-op when the task is unassigned or assigned to the actor themselves. Never throws —
    /// a notification hiccup must not fail the checklist write.
    /// </summary>
    private async Task NotifyTaskAssignedAsync(BidChecklistItem item, Guid assignerId, CancellationToken ct)
    {
        if (item.AssignedTo is not Guid assigneeId || assigneeId == assignerId)
            return;

        try
        {
            var assignee = await db.Users.FirstOrDefaultAsync(u => u.Id == assigneeId, ct);
            if (assignee is null) return;

            var bidTitle   = await db.Bids.Where(b => b.Id == item.BidId).Select(b => b.Title).FirstOrDefaultAsync(ct) ?? "a bid";
            var assignerName = await GetUserNameAsync(assignerId, ct) ?? "A teammate";
            var dueText = item.DueDate is DateOnly due ? $" It is due on {due:dd MMM yyyy}." : string.Empty;

            var recipients = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.InApp, assigneeId.ToString()),
            };
            if (await ShouldEmailTaskAssignedAsync(assigneeId, item.OrgId, ct))
                recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, assignee.Email));

            await notificationPublisher.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "TASK_ASSIGNED",
                UserId:       assigneeId,
                Payload: new Dictionary<string, object>
                {
                    ["AssigneeName"]   = assignee.Name,
                    ["TaskTitle"]      = item.Title,
                    ["BidTitle"]       = bidTitle,
                    ["BidId"]          = item.BidId.ToString(),
                    ["DueText"]        = dueText,
                    ["AssignedByName"] = assignerName,
                    ["ActionUrl"]      = $"/bids/{item.BidId}",
                },
                Recipients: recipients), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TASK_ASSIGNED notification failed for item {ItemId}", item.Id);
        }
    }

    /// <summary>
    /// Email for task-assigned is opt-OUT (default on). A user disables it by setting their
    /// email channel preference to disabled, or by curating an explicit event list that
    /// excludes <c>task_assigned</c>.
    /// </summary>
    private async Task<bool> ShouldEmailTaskAssignedAsync(Guid userId, Guid orgId, CancellationToken ct)
    {
        var pref = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrgId == orgId && p.Channel == "email", ct);
        if (pref is null) return true;
        if (!pref.IsEnabled) return false;
        if (pref.EventTypes.Length > 0 && !pref.EventTypes.Contains("task_assigned")) return false;
        return true;
    }

    // ── Bid notifications (assignment + stage change / won / lost) ───────────────

    /// <summary>
    /// Fire a BID_ASSIGNED notification to the new assignee (InApp always; Email unless they
    /// opted out). No-op when the bid is unassigned or assigned to the actor themselves. Never
    /// throws — a notification hiccup must not fail the bid update.
    /// </summary>
    private async Task NotifyBidAssignedAsync(Bid bid, Guid assignerId, CancellationToken ct)
    {
        if (bid.AssignedTo is not Guid assigneeId || assigneeId == assignerId)
            return;

        try
        {
            var assignee = await audience.ByUserAsync(assigneeId, ct);
            if (assignee is null) return;

            var assignerName = await GetUserNameAsync(assignerId, ct) ?? "A teammate";
            var dueText = bid.DueDate is DateOnly due ? $"on {due:dd MMM yyyy}" : string.Empty;

            var recipients = new List<NotificationRecipientDto>
            {
                new(NotificationChannel.InApp, assigneeId.ToString()),
            };
            if (!string.IsNullOrWhiteSpace(assignee.Email)
                && await ShouldEmailAsync(assigneeId, bid.OrgId, "bid_assigned", ct))
                recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, assignee.Email!));

            await notificationPublisher.SendAsync(new SendNotificationDto(
                Category:     NotificationCategory.Transactional,
                TemplateCode: "BID_ASSIGNED",
                UserId:       assigneeId,
                Payload: new Dictionary<string, object>
                {
                    ["FirstName"]      = FirstNameOf(assignee.Name),
                    ["BidTitle"]       = bid.Title,
                    ["AssignedByName"] = assignerName,
                    ["DueText"]        = dueText,
                    ["OrgId"]          = bid.OrgId.ToString(),
                    ["EntityId"]       = bid.Id.ToString(),
                    ["Link"]           = $"{FrontendBaseUrl}/bids/{bid.Id}",
                },
                Recipients: recipients), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BID_ASSIGNED notification failed for bid {BidId}", bid.Id);
        }
    }

    /// <summary>
    /// On a stage transition, notify the assignee (BID_STAGE_CHANGED, in-app). Won/lost are
    /// outcomes the whole bid team cares about, so they also fan out to owner/admin/bid_manager
    /// over Email + InApp. The acting user is never notified about their own change. Never throws.
    /// </summary>
    private async Task NotifyStageChangedAsync(Bid bid, string fromStage, string toStage, Guid actorId, CancellationToken ct)
    {
        if (string.Equals(fromStage, toStage, StringComparison.Ordinal))
            return;

        var (template, category) = toStage switch
        {
            "won"  => ("BID_WON",  NotificationCategory.Information),
            "lost" => ("BID_LOST", NotificationCategory.Information),
            _      => ("BID_STAGE_CHANGED", NotificationCategory.Information),
        };
        var isOutcome = template is "BID_WON" or "BID_LOST";

        try
        {
            // De-dup recipients by user id (assignee may also hold a notified role).
            var targets = new Dictionary<Guid, NotificationAudienceMember>();
            if (bid.AssignedTo is Guid a && a != actorId)
            {
                var m = await audience.ByUserAsync(a, ct);
                if (m is not null) targets[m.UserId] = m;
            }
            if (isOutcome)
                foreach (var m in await audience.ByRolesAsync(bid.OrgId, BidNotifyRoles, actorId, ct))
                    targets[m.UserId] = m;

            if (targets.Count == 0) return;

            foreach (var m in targets.Values)
            {
                var recipients = new List<NotificationRecipientDto>
                {
                    new(NotificationChannel.InApp, m.UserId.ToString()),
                };
                if (isOutcome && !string.IsNullOrWhiteSpace(m.Email))
                    recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, m.Email!));

                try
                {
                    await notificationPublisher.SendAsync(new SendNotificationDto(
                        Category:     category,
                        TemplateCode: template,
                        UserId:       m.UserId,
                        Payload: new Dictionary<string, object>
                        {
                            ["FirstName"]  = FirstNameOf(m.Name),
                            ["BidTitle"]   = bid.Title,
                            ["FromStage"]  = fromStage,
                            ["ToStage"]    = toStage,
                            ["WonValue"]   = bid.WonValue?.ToString("N0") ?? string.Empty,
                            ["LossReason"] = bid.LossReason ?? string.Empty,
                            ["OrgId"]      = bid.OrgId.ToString(),
                            ["EntityId"]   = bid.Id.ToString(),
                            ["Link"]       = $"{FrontendBaseUrl}/bids/{bid.Id}",
                        },
                        Recipients: recipients), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "{Template} notification failed for bid {BidId}, user {UserId}", template, bid.Id, m.UserId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stage-change notification failed for bid {BidId}", bid.Id);
        }
    }

    /// <summary>
    /// Notify on a new comment (BID_COMMENT, InApp + opt-out email). Recipients are the bid's
    /// assignee plus everyone @-mentioned in the note, de-duplicated and excluding the author.
    /// No-op when there's no one left to notify. Never throws.
    /// </summary>
    private async Task NotifyBidCommentAsync(
        Guid bidId, Guid orgId, Guid authorId, string authorName, string body,
        IReadOnlyCollection<Guid> mentionedUserIds, CancellationToken ct)
    {
        try
        {
            var bid = await db.Bids.AsNoTracking()
                .Where(b => b.Id == bidId)
                .Select(b => new { b.Title, b.AssignedTo })
                .FirstOrDefaultAsync(ct);
            if (bid is null) return;

            // Assignee (unless they wrote the note) + everyone @-mentioned, minus the author.
            var targetIds = new HashSet<Guid>();
            if (bid.AssignedTo is Guid assigneeId && assigneeId != authorId)
                targetIds.Add(assigneeId);
            foreach (var uid in mentionedUserIds)
                if (uid != authorId) targetIds.Add(uid);
            if (targetIds.Count == 0) return;

            var snippet = body.Length > 140 ? body[..140] + "…" : body;

            foreach (var userId in targetIds)
            {
                var member = await audience.ByUserAsync(userId, ct);
                if (member is null) continue;

                var recipients = new List<NotificationRecipientDto>
                {
                    new(NotificationChannel.InApp, userId.ToString()),
                };
                if (!string.IsNullOrWhiteSpace(member.Email)
                    && await ShouldEmailAsync(userId, orgId, "bid_comment", ct))
                    recipients.Add(new NotificationRecipientDto(NotificationChannel.Email, member.Email!));

                try
                {
                    await notificationPublisher.SendAsync(new SendNotificationDto(
                        Category:     NotificationCategory.Information,
                        TemplateCode: "BID_COMMENT",
                        UserId:       userId,
                        Payload: new Dictionary<string, object>
                        {
                            ["FirstName"]  = FirstNameOf(member.Name),
                            ["BidTitle"]   = bid.Title,
                            ["AuthorName"] = authorName,
                            ["Snippet"]    = snippet,
                            ["OrgId"]      = orgId.ToString(),
                            ["EntityId"]   = bidId.ToString(),
                            ["Link"]       = $"{FrontendBaseUrl}/bids/{bidId}",
                        },
                        Recipients: recipients), ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "BID_COMMENT notification failed for bid {BidId}, user {UserId}", bidId, userId);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BID_COMMENT notification failed for bid {BidId}", bidId);
        }
    }

    /// <summary>
    /// Email opt-out check (default on). A user disables it by setting their email channel
    /// preference to disabled, or by curating an explicit event list that excludes
    /// <paramref name="eventType"/>.
    /// </summary>
    private async Task<bool> ShouldEmailAsync(Guid userId, Guid orgId, string eventType, CancellationToken ct)
    {
        var pref = await db.NotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.OrgId == orgId && p.Channel == "email", ct);
        if (pref is null) return true;
        if (!pref.IsEnabled) return false;
        if (pref.EventTypes.Length > 0 && !pref.EventTypes.Contains(eventType)) return false;
        return true;
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];

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
    /// Ensures a row exists in the local PostgreSQL <c>tenders</c> table for the given id and
    /// returns the id the <c>bids.tender_id</c> foreign key must point at. The canonical tender
    /// lives in MongoDB via BiddingBuddyServices; we mirror a minimal stub here so the FK resolves.
    /// </summary>
    /// <remarks>
    /// The id the UI sends (the Mongo <c>_id</c>) is a different id-space than the Postgres
    /// primary key the pipeline assigns when it mirrors a tender (<see cref="InternalPipelineService"/>
    /// inserts with <c>gen_random_uuid()</c>). So a row for this tender may already exist under a
    /// <em>different</em> PK but the same <c>gem_tender_id</c>/<c>mongo_tender_id</c>. Inserting a
    /// second stub in that case violates the <c>tenders_gem_tender_id_key</c> unique constraint, so
    /// we resolve the existing row by its natural keys and reuse its id instead of duplicating it.
    /// </remarks>
    private async Task<Guid> EnsureLocalTenderAsync(Guid tenderId, CancellationToken ct)
    {
        // Already mirrored under this exact id — the FK resolves as-is.
        if (await db.Tenders.AnyAsync(t => t.Id == tenderId, ct))
            return tenderId;

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

        var mongoId = tenderId.ToString();
        var gemId   = string.IsNullOrWhiteSpace(remote.GemTenderId) ? mongoId : remote.GemTenderId;

        // The pipeline may already have mirrored this tender under a Postgres-generated id but
        // the same gem_tender_id / mongo_tender_id. Reuse that row rather than insert a duplicate
        // (which would violate tenders_gem_tender_id_key — the bug this guards against).
        var existing = await db.Tenders
            .FirstOrDefaultAsync(t => t.GemTenderId == gemId || t.MongoTenderId == mongoId, ct);
        if (existing is not null)
        {
            // Reusing a pipeline-mirrored row (its PK ≠ the Mongo id the UI sent). Stamp its
            // mongo_tender_id from the id we just resolved if the pipeline hasn't backfilled it,
            // so GetByTenderIdsAsync can later match this tender by the Mongo id the UI queries
            // with — i.e. the "already in pipeline" badge lights up without depending on the
            // global migration-0010 backfill having run.
            if (string.IsNullOrWhiteSpace(existing.MongoTenderId))
            {
                existing.MongoTenderId = mongoId;
                await db.SaveChangesAsync(ct);
            }
            return existing.Id;
        }

        var now = DateTime.UtcNow;
        var stub = new Tender
        {
            Id               = tenderId,
            GemTenderId      = gemId,
            MongoTenderId    = mongoId,
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with the pipeline upsert (it inserted the same gem_tender_id
            // between our existence check and this save). Drop our stub and reuse the row
            // that won the race so the bid still gets a valid FK.
            entry.State = EntityState.Detached;
            var raced = await db.Tenders
                .FirstOrDefaultAsync(t => t.GemTenderId == gemId || t.MongoTenderId == mongoId, ct);
            if (raced is null) throw;
            return raced.Id;
        }

        return tenderId;
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
            bid.Tender?.GemTenderId, bid.Tender?.MongoTenderId, bid.Title, bid.Description,
            bid.Stage, bid.StatusCategory, bid.Priority,
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
