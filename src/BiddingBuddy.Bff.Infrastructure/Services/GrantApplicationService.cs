using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public partial class GrantApplicationService(BffDbContext db) : IGrantApplicationService
{
    // The plan checklist seeded on a new application — the standard federal-grant pursuit steps.
    private static readonly string[] DefaultChecklist =
    [
        "Confirm eligibility & registrations (SAM/UEI)",
        "Gather required documents",
        "Draft the project narrative",
        "Build the budget & justification",
        "Internal review",
        "Submit before the deadline",
    ];

    // ── List / board ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantApplicationListItemDto>> ListAsync(
        Guid orgId, GrantApplicationListQuery q, CancellationToken ct = default)
    {
        var query = db.GrantApplications.Where(a => a.OrgId == orgId);

        if (!string.IsNullOrWhiteSpace(q.Stage))
        {
            if (!GrantApplicationStages.IsValid(q.Stage))
                throw new ArgumentException($"Invalid stage '{q.Stage}'. Allowed: {string.Join(", ", GrantApplicationStages.All)}.", nameof(q));
            query = query.Where(a => a.Stage == q.Stage);
        }
        if (!string.IsNullOrWhiteSpace(q.StatusCategory))
        {
            if (!GrantApplicationStages.IsValidStatusCategory(q.StatusCategory))
                throw new ArgumentException($"Invalid statusCategory '{q.StatusCategory}'. Allowed: open, closed.", nameof(q));
            query = query.Where(a => a.StatusCategory == q.StatusCategory);
        }
        if (q.AssignedTo.HasValue) query = query.Where(a => a.AssignedTo == q.AssignedTo);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var like = $"%{q.Q.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.Title, like) ||
                (a.AgencyName != null && EF.Functions.ILike(a.AgencyName, like)));
        }

        var rows = await ApplySort(query, q.Sort).Include(a => a.AssignedUser).ToListAsync(ct);
        return rows.Select(MapListItem).ToList();
    }

    private static IQueryable<GrantApplication> ApplySort(IQueryable<GrantApplication> query, string? sort)
    {
        var key = sort?.Trim();
        if (string.IsNullOrEmpty(key)) return query.OrderByDescending(a => a.UpdatedAt);
        var desc = key[0] == '-';
        if (desc) key = key[1..];
        return key switch
        {
            "updatedAt" => desc ? query.OrderByDescending(a => a.UpdatedAt) : query.OrderBy(a => a.UpdatedAt),
            "deadline"  => desc ? query.OrderByDescending(a => a.Deadline)  : query.OrderBy(a => a.Deadline),
            "title"     => desc ? query.OrderByDescending(a => a.Title)     : query.OrderBy(a => a.Title),
            "readiness" => desc ? query.OrderByDescending(a => a.Readiness) : query.OrderBy(a => a.Readiness),
            "amount"    => desc ? query.OrderByDescending(a => a.AmountRequested) : query.OrderBy(a => a.AmountRequested),
            _ => throw new ArgumentException($"Invalid sort '{sort}'. Allowed: updatedAt, deadline, title, readiness, amount (optionally '-' prefixed).", nameof(sort)),
        };
    }

    // ── Detail ────────────────────────────────────────────────────────────────

    public async Task<GrantApplicationDetailDto> GetAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        var app = await LoadAsync(id, orgId, ct);
        return await MapToDetailAsync(app, ct);
    }

    public async Task<GrantApplicationDetailDto> CreateAsync(
        Guid orgId, Guid userId, CreateGrantApplicationDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("title is required.", nameof(dto));
        if (!GrantApplicationStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'. Allowed: {string.Join(", ", GrantApplicationStages.All)}.", nameof(dto));

        // Starting an application from a grant is idempotent — one open application per grant per org.
        if (!string.IsNullOrWhiteSpace(dto.MongoGrantId))
        {
            var existing = await db.GrantApplications
                .Where(a => a.OrgId == orgId && a.MongoGrantId == dto.MongoGrantId && a.StatusCategory == "open")
                .OrderByDescending(a => a.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            if (existing is not null) return await GetAsync(existing.Id, orgId, ct);
        }

        var app = new GrantApplication
        {
            OrgId = orgId,
            MongoGrantId = string.IsNullOrWhiteSpace(dto.MongoGrantId) ? null : dto.MongoGrantId,
            Title = dto.Title,
            AgencyName = dto.AgencyName,
            OpportunityNumber = dto.OpportunityNumber,
            Stage = dto.Stage,
            AmountRequested = dto.AmountRequested,
            Deadline = dto.Deadline,
            AssignedTo = dto.AssignedTo,
            CostSharePct = dto.CostSharePct,
            TribalSetAside = dto.TribalSetAside,
            Tags = dto.Tags?.ToArray(),
            SourceUrl = dto.SourceUrl,
            Notes = dto.Notes,
            CreatedBy = userId,
            Readiness = 0,
        };
        db.GrantApplications.Add(app);

        // Children link by navigation, not app.Id — the id is store-generated (gen_random_uuid()),
        // so before SaveChanges the scalar gives EF no edge to order the inserts by and it can emit
        // a child first → 23503 FK violation.
        db.GrantApplicationActivities.Add(new GrantApplicationActivity
        {
            Application = app,
            OrgId = orgId,
            ActorId = userId,
            Action = "created",
        });

        var order = 0;
        foreach (var title in DefaultChecklist)
        {
            db.GrantApplicationChecklistItems.Add(new GrantApplicationChecklistItem
            {
                Application = app,
                OrgId = orgId,
                Title = title,
                SortOrder = order++,
            });
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(app.Id, orgId, ct);
    }

    public async Task<GrantApplicationDetailDto> UpdateAsync(
        Guid id, Guid orgId, Guid userId, UpdateGrantApplicationDto dto, CancellationToken ct = default)
    {
        var app = await LoadAsync(id, orgId, ct);

        if (dto.Stage is not null && !GrantApplicationStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'.", nameof(dto));

        if (dto.Title is not null) app.Title = dto.Title;
        if (dto.AgencyName is not null) app.AgencyName = dto.AgencyName;
        if (dto.Stage is not null) app.Stage = dto.Stage;
        if (dto.AmountRequested.HasValue) app.AmountRequested = dto.AmountRequested;
        if (dto.Deadline.HasValue) app.Deadline = dto.Deadline;
        if (dto.AssignedTo.HasValue) app.AssignedTo = dto.AssignedTo;
        if (dto.CostSharePct.HasValue) app.CostSharePct = dto.CostSharePct;
        if (dto.TribalSetAside.HasValue) app.TribalSetAside = dto.TribalSetAside.Value;
        if (dto.Tags is not null) app.Tags = dto.Tags.ToArray();
        if (dto.Notes is not null) app.Notes = dto.Notes;

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, orgId, ct);
    }

    public async Task<GrantApplicationDetailDto> ChangeStageAsync(
        Guid id, Guid orgId, Guid userId, ChangeGrantApplicationStageDto dto, CancellationToken ct = default)
    {
        if (!GrantApplicationStages.IsValid(dto.Stage))
            throw new ArgumentException($"Invalid stage '{dto.Stage}'. Allowed: {string.Join(", ", GrantApplicationStages.All)}.", nameof(dto));

        var app = await LoadAsync(id, orgId, ct);
        var from = app.Stage;
        if (from == dto.Stage) return await MapToDetailAsync(app, ct);

        app.Stage = dto.Stage;
        db.GrantApplicationActivities.Add(new GrantApplicationActivity
        {
            ApplicationId = id,
            OrgId = orgId,
            ActorId = userId,
            Action = "stage_change",
            FromValue = from,
            ToValue = dto.Stage,
            Note = dto.Note,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, orgId, ct);
    }

    public async Task DeleteAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        var app = await LoadAsync(id, orgId, ct);
        db.GrantApplications.Remove(app);
        await db.SaveChangesAsync(ct);
    }

    // ── Activities ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantApplicationActivityDto>> GetActivitiesAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        return await db.GrantApplicationActivities
            .Include(a => a.Actor)
            .Where(a => a.ApplicationId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new GrantApplicationActivityDto(
                a.Id, a.ActorId, a.Actor != null ? a.Actor.Name : null,
                a.Action, a.FromValue, a.ToValue, a.Note, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<GrantApplicationActivityDto> AddNoteAsync(
        Guid id, Guid orgId, Guid userId, AddGrantApplicationNoteDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        if (string.IsNullOrWhiteSpace(dto.Note))
            throw new ArgumentException("note cannot be empty.", nameof(dto));

        var activity = new GrantApplicationActivity
        {
            ApplicationId = id,
            OrgId = orgId,
            ActorId = userId,
            Action = "note",
            Note = dto.Note.Trim(),
        };
        db.GrantApplicationActivities.Add(activity);
        await db.SaveChangesAsync(ct);

        var name = await GetUserNameAsync(userId, ct);
        return new GrantApplicationActivityDto(
            activity.Id, userId, name, "note", null, null, activity.Note, activity.CreatedAt);
    }

    // ── Checklist ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantChecklistItemDto>> GetChecklistAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var items = await db.GrantApplicationChecklistItems
            .Where(i => i.ApplicationId == id)
            .OrderBy(i => i.SortOrder)
            .ToListAsync(ct);
        var names = await UserNamesAsync(items.Where(i => i.AssignedTo.HasValue).Select(i => i.AssignedTo!.Value), ct);
        return items.Select(i => MapChecklist(i, i.AssignedTo.HasValue ? names.GetValueOrDefault(i.AssignedTo.Value) : null)).ToList();
    }

    public async Task<GrantChecklistItemDto> CreateChecklistItemAsync(
        Guid id, Guid orgId, Guid userId, CreateGrantChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("title is required.", nameof(dto));

        var item = new GrantApplicationChecklistItem
        {
            ApplicationId = id,
            OrgId = orgId,
            Title = dto.Title,
            DueDate = dto.DueDate,
            AssignedTo = dto.AssignedTo,
            SortOrder = dto.SortOrder,
        };
        db.GrantApplicationChecklistItems.Add(item);
        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task<GrantChecklistItemDto> UpdateChecklistItemAsync(
        Guid itemId, Guid id, Guid orgId, Guid userId, UpdateGrantChecklistItemDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var item = await db.GrantApplicationChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");

        if (dto.Title is not null) item.Title = dto.Title;
        if (dto.DueDate.HasValue) item.DueDate = dto.DueDate;
        if (dto.AssignedTo.HasValue) item.AssignedTo = dto.AssignedTo;
        if (dto.SortOrder.HasValue) item.SortOrder = dto.SortOrder.Value;
        if (dto.IsDone.HasValue)
        {
            item.IsDone = dto.IsDone.Value;
            item.DoneAt = dto.IsDone.Value ? DateTime.UtcNow : null;
            item.DoneBy = dto.IsDone.Value ? userId : null;
        }

        await db.SaveChangesAsync(ct);
        if (dto.IsDone.HasValue) await RecomputeReadinessAsync(id, orgId, ct);
        return MapChecklist(item, await GetUserNameAsync(item.AssignedTo, ct));
    }

    public async Task DeleteChecklistItemAsync(Guid itemId, Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var item = await db.GrantApplicationChecklistItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Checklist item not found.");
        db.GrantApplicationChecklistItems.Remove(item);
        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
    }

    // ── Readiness ─────────────────────────────────────────────────────────────
    //
    // Proposal-readiness folds four signals: checklist completion (0.4), narrative-section
    // completion (0.4), whether a budget exists (0.1), and whether any review is approved (0.1).
    private async Task RecomputeReadinessAsync(Guid applicationId, Guid orgId, CancellationToken ct)
    {
        var app = await db.GrantApplications
            .FirstOrDefaultAsync(a => a.Id == applicationId && a.OrgId == orgId, ct);
        if (app is null) return;

        var checklistTotal = await db.GrantApplicationChecklistItems.CountAsync(i => i.ApplicationId == applicationId, ct);
        var checklistDone = await db.GrantApplicationChecklistItems.CountAsync(i => i.ApplicationId == applicationId && i.IsDone, ct);
        var checklistPct = checklistTotal == 0 ? 0.0 : (double)checklistDone / checklistTotal;

        var narrativeTotal = await db.GrantNarrativeSections.CountAsync(s => s.ApplicationId == applicationId, ct);
        var narrativeComplete = await db.GrantNarrativeSections.CountAsync(s => s.ApplicationId == applicationId && s.Status == "complete", ct);
        var narrativePct = narrativeTotal == 0 ? 0.0 : (double)narrativeComplete / narrativeTotal;

        var hasBudget = await db.GrantBudgetLineItems.AnyAsync(l => l.ApplicationId == applicationId, ct) ? 1.0 : 0.0;
        var hasApproved = await db.GrantReviews.AnyAsync(r => r.ApplicationId == applicationId && r.Status == "approved", ct) ? 1.0 : 0.0;

        var score = (0.4 * checklistPct) + (0.4 * narrativePct) + (0.1 * hasBudget) + (0.1 * hasApproved);
        app.Readiness = (int)Math.Round(100 * score);
        await db.SaveChangesAsync(ct);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private async Task<GrantApplication> LoadAsync(Guid id, Guid orgId, CancellationToken ct)
        => await db.GrantApplications
            .Include(a => a.AssignedUser)
            .FirstOrDefaultAsync(a => a.Id == id && a.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Application not found.");

    private async Task EnsureBelongsAsync(Guid id, Guid orgId, CancellationToken ct)
    {
        var exists = await db.GrantApplications.AnyAsync(a => a.Id == id && a.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Application not found.");
    }

    private async Task<GrantApplicationDetailDto> MapToDetailAsync(GrantApplication a, CancellationToken ct)
    {
        var recent = await db.GrantApplicationActivities
            .Include(x => x.Actor)
            .Where(x => x.ApplicationId == a.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(15)
            .Select(x => new GrantApplicationActivityDto(
                x.Id, x.ActorId, x.Actor != null ? x.Actor.Name : null,
                x.Action, x.FromValue, x.ToValue, x.Note, x.CreatedAt))
            .ToListAsync(ct);

        var checklist = await GetChecklistAsync(a.Id, a.OrgId, ct);

        return new GrantApplicationDetailDto(
            a.Id, a.OrgId, a.MongoGrantId, a.Title, a.AgencyName, a.OpportunityNumber,
            a.Stage, a.StatusCategory, a.AmountRequested, a.Deadline,
            a.AssignedTo, a.AssignedUser?.Name, a.Readiness, a.CostSharePct, a.TribalSetAside,
            a.Tags ?? [], a.SourceUrl, a.Notes, a.CreatedAt, a.UpdatedAt, recent, checklist);
    }

    private static GrantApplicationListItemDto MapListItem(GrantApplication a) => new(
        a.Id, a.MongoGrantId, a.Title, a.AgencyName, a.OpportunityNumber, a.Stage, a.StatusCategory,
        a.AmountRequested, a.Deadline, a.AssignedTo, a.AssignedUser?.Name, a.Readiness,
        a.CostSharePct, a.TribalSetAside, a.Tags ?? [], a.UpdatedAt);

    private static GrantChecklistItemDto MapChecklist(GrantApplicationChecklistItem i, string? assignedName)
        => new(i.Id, i.Title, i.IsDone, i.DueDate, i.AssignedTo, assignedName, i.DoneAt, i.SortOrder);

    private async Task<string?> GetUserNameAsync(Guid? userId, CancellationToken ct)
    {
        if (!userId.HasValue) return null;
        return await db.Users.Where(u => u.Id == userId.Value).Select(u => u.Name).FirstOrDefaultAsync(ct);
    }

    private async Task<Dictionary<Guid, string>> UserNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Users.Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
}
