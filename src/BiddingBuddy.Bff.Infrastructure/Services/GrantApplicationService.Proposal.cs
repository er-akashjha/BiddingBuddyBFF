using BiddingBuddy.Bff.Core.Constants;
using BiddingBuddy.Bff.Core.DTOs.Grants;
using BiddingBuddy.Bff.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

// Proposal-authoring surfaces of the grant application: narrative, budget, reviews, submissions.
public partial class GrantApplicationService
{
    private static readonly string[] BudgetCategories =
        ["personnel", "fringe", "travel", "equipment", "supplies", "contractual", "indirect", "other"];
    private static readonly string[] NarrativeStatuses = ["not_started", "drafting", "complete"];
    private static readonly string[] ReviewStatuses = ["pending", "in_progress", "approved", "changes_requested"];
    private static readonly string[] SubmissionPortals = ["grants_gov", "foundation", "submittable", "fluxx", "other"];
    private static readonly string[] SubmissionStatuses = ["draft", "submitted", "under_review", "awarded", "declined", "more_info"];

    // The standard federal-proposal sections, seeded on first read of an application's narrative.
    private static readonly (string Key, string Title, int Target)[] DefaultSections =
    [
        ("need_statement", "Statement of need", 500),
        ("project_description", "Project description", 1500),
        ("goals_outcomes", "Goals & measurable outcomes", 600),
        ("evaluation_plan", "Evaluation plan", 500),
        ("sustainability", "Sustainability plan", 400),
        ("org_capacity", "Organizational capacity", 400),
    ];

    private static int WordsIn(string? content) =>
        string.IsNullOrWhiteSpace(content) ? 0 : content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ── Narrative ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantNarrativeSectionDto>> GetNarrativeAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);

        var existing = await db.GrantNarrativeSections
            .Where(s => s.ApplicationId == id)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        // Seed the default section set the first time the tab is opened (also backfills applications
        // created before the narrative table existed).
        if (existing.Count == 0)
        {
            var order = 0;
            foreach (var (key, title, target) in DefaultSections)
            {
                db.GrantNarrativeSections.Add(new GrantNarrativeSection
                {
                    ApplicationId = id, OrgId = orgId, SectionKey = key, Title = title,
                    TargetWords = target, SortOrder = order++,
                });
            }
            await db.SaveChangesAsync(ct);
            existing = await db.GrantNarrativeSections
                .Where(s => s.ApplicationId == id)
                .OrderBy(s => s.SortOrder)
                .ToListAsync(ct);
        }

        return existing.Select(MapNarrative).ToList();
    }

    public async Task<GrantNarrativeSectionDto> UpdateNarrativeSectionAsync(
        Guid sectionId, Guid id, Guid orgId, Guid userId, UpdateNarrativeSectionDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var section = await db.GrantNarrativeSections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Narrative section not found.");

        if (dto.Status is not null && !NarrativeStatuses.Contains(dto.Status))
            throw new ArgumentException($"Invalid status '{dto.Status}'. Allowed: {string.Join(", ", NarrativeStatuses)}.", nameof(dto));

        if (dto.Title is not null) section.Title = dto.Title;
        if (dto.TargetWords.HasValue) section.TargetWords = dto.TargetWords;
        if (dto.Content is not null)
        {
            section.Content = dto.Content;
            section.WordCount = WordsIn(dto.Content);
        }
        if (dto.Status is not null) section.Status = dto.Status;
        section.UpdatedBy = userId;

        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
        return MapNarrative(section);
    }

    // ── Budget ─────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantBudgetLineDto>> GetBudgetAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        return await db.GrantBudgetLineItems
            .Where(l => l.ApplicationId == id)
            .OrderBy(l => l.SortOrder)
            .Select(l => new GrantBudgetLineDto(l.Id, l.Category, l.Description, l.Amount, l.SortOrder))
            .ToListAsync(ct);
    }

    public async Task<GrantBudgetLineDto> AddBudgetLineAsync(Guid id, Guid orgId, CreateBudgetLineDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var category = NormalizeBudgetCategory(dto.Category);
        if (string.IsNullOrWhiteSpace(dto.Description))
            throw new ArgumentException("description is required.", nameof(dto));

        var line = new GrantBudgetLineItem
        {
            ApplicationId = id, OrgId = orgId, Category = category,
            Description = dto.Description, Amount = dto.Amount, SortOrder = dto.SortOrder,
        };
        db.GrantBudgetLineItems.Add(line);
        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
        return new GrantBudgetLineDto(line.Id, line.Category, line.Description, line.Amount, line.SortOrder);
    }

    public async Task<GrantBudgetLineDto> UpdateBudgetLineAsync(Guid lineId, Guid id, Guid orgId, UpdateBudgetLineDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var line = await db.GrantBudgetLineItems
            .FirstOrDefaultAsync(l => l.Id == lineId && l.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Budget line not found.");

        if (dto.Category is not null) line.Category = NormalizeBudgetCategory(dto.Category);
        if (dto.Description is not null) line.Description = dto.Description;
        if (dto.Amount.HasValue) line.Amount = dto.Amount.Value;
        if (dto.SortOrder.HasValue) line.SortOrder = dto.SortOrder.Value;

        await db.SaveChangesAsync(ct);
        return new GrantBudgetLineDto(line.Id, line.Category, line.Description, line.Amount, line.SortOrder);
    }

    public async Task DeleteBudgetLineAsync(Guid lineId, Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var line = await db.GrantBudgetLineItems
            .FirstOrDefaultAsync(l => l.Id == lineId && l.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Budget line not found.");
        db.GrantBudgetLineItems.Remove(line);
        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
    }

    private static string NormalizeBudgetCategory(string category)
    {
        var c = (category ?? string.Empty).Trim().ToLowerInvariant();
        if (!BudgetCategories.Contains(c))
            throw new ArgumentException($"Invalid category '{category}'. Allowed: {string.Join(", ", BudgetCategories)}.", nameof(category));
        return c;
    }

    // ── Reviews ────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantReviewDto>> GetReviewsAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        return await db.GrantReviews
            .Include(r => r.Reviewer)
            .Where(r => r.ApplicationId == id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new GrantReviewDto(
                r.Id, r.ReviewerId, r.Reviewer != null ? r.Reviewer.Name : null,
                r.Status, r.Comments, r.ReviewedAt, r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<GrantReviewDto> AddReviewAsync(Guid id, Guid orgId, Guid userId, CreateReviewDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var review = new GrantReview
        {
            ApplicationId = id, OrgId = orgId, ReviewerId = dto.ReviewerId,
            Comments = dto.Comments, Status = "pending", CreatedBy = userId,
        };
        db.GrantReviews.Add(review);
        await db.SaveChangesAsync(ct);
        var name = await GetUserNameAsync(review.ReviewerId, ct);
        return new GrantReviewDto(review.Id, review.ReviewerId, name, review.Status, review.Comments, review.ReviewedAt, review.CreatedAt);
    }

    public async Task<GrantReviewDto> UpdateReviewAsync(Guid reviewId, Guid id, Guid orgId, Guid userId, UpdateReviewDto dto, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var review = await db.GrantReviews
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Review not found.");

        if (dto.Status is not null)
        {
            if (!ReviewStatuses.Contains(dto.Status))
                throw new ArgumentException($"Invalid status '{dto.Status}'. Allowed: {string.Join(", ", ReviewStatuses)}.", nameof(dto));
            review.Status = dto.Status;
            review.ReviewedAt = dto.Status is "approved" or "changes_requested" ? DateTime.UtcNow : review.ReviewedAt;
        }
        if (dto.Comments is not null) review.Comments = dto.Comments;

        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
        var name = await GetUserNameAsync(review.ReviewerId, ct);
        return new GrantReviewDto(review.Id, review.ReviewerId, name, review.Status, review.Comments, review.ReviewedAt, review.CreatedAt);
    }

    public async Task DeleteReviewAsync(Guid reviewId, Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var review = await db.GrantReviews
            .FirstOrDefaultAsync(r => r.Id == reviewId && r.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Review not found.");
        db.GrantReviews.Remove(review);
        await db.SaveChangesAsync(ct);
        await RecomputeReadinessAsync(id, orgId, ct);
    }

    // ── Submissions ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<GrantSubmissionDto>> GetSubmissionsAsync(Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var rows = await db.GrantSubmissions
            .Where(s => s.ApplicationId == id)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
        var names = await UserNamesAsync(rows.Where(s => s.SubmittedBy.HasValue).Select(s => s.SubmittedBy!.Value), ct);
        return rows.Select(s => MapSubmission(s, s.SubmittedBy.HasValue ? names.GetValueOrDefault(s.SubmittedBy.Value) : null)).ToList();
    }

    public async Task<GrantSubmissionDto> AddSubmissionAsync(Guid id, Guid orgId, Guid userId, CreateSubmissionDto dto, CancellationToken ct = default)
    {
        var app = await LoadAsync(id, orgId, ct);
        var portal = NormalizeSubmissionPortal(dto.Portal);
        var status = NormalizeSubmissionStatus(dto.Status);

        var sub = new GrantSubmission
        {
            ApplicationId = id, OrgId = orgId, Portal = portal, Status = status,
            ConfirmationNumber = dto.ConfirmationNumber, AmountAwarded = dto.AmountAwarded, Notes = dto.Notes,
        };
        if (status is not "draft")
        {
            sub.SubmittedAt = DateTime.UtcNow;
            sub.SubmittedBy = userId;
        }
        db.GrantSubmissions.Add(sub);
        await db.SaveChangesAsync(ct);

        await MaybeAdvanceStageAsync(app, status, userId, ct);
        return MapSubmission(sub, await GetUserNameAsync(sub.SubmittedBy, ct));
    }

    public async Task<GrantSubmissionDto> UpdateSubmissionAsync(Guid submissionId, Guid id, Guid orgId, Guid userId, UpdateSubmissionDto dto, CancellationToken ct = default)
    {
        var app = await LoadAsync(id, orgId, ct);
        var sub = await db.GrantSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Submission not found.");

        if (dto.Portal is not null) sub.Portal = NormalizeSubmissionPortal(dto.Portal);
        if (dto.ConfirmationNumber is not null) sub.ConfirmationNumber = dto.ConfirmationNumber;
        if (dto.AmountAwarded.HasValue) sub.AmountAwarded = dto.AmountAwarded;
        if (dto.Notes is not null) sub.Notes = dto.Notes;
        if (dto.Status is not null)
        {
            var status = NormalizeSubmissionStatus(dto.Status);
            var wasDraft = sub.Status == "draft";
            sub.Status = status;
            if (status is not "draft" && wasDraft && sub.SubmittedAt is null)
            {
                sub.SubmittedAt = DateTime.UtcNow;
                sub.SubmittedBy = userId;
            }
            await db.SaveChangesAsync(ct);
            await MaybeAdvanceStageAsync(app, status, userId, ct);
        }
        else
        {
            await db.SaveChangesAsync(ct);
        }

        return MapSubmission(sub, await GetUserNameAsync(sub.SubmittedBy, ct));
    }

    public async Task DeleteSubmissionAsync(Guid submissionId, Guid id, Guid orgId, CancellationToken ct = default)
    {
        await EnsureBelongsAsync(id, orgId, ct);
        var sub = await db.GrantSubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId && s.ApplicationId == id, ct)
            ?? throw new KeyNotFoundException("Submission not found.");
        db.GrantSubmissions.Remove(sub);
        await db.SaveChangesAsync(ct);
    }

    // Recording a submission moves the application stage to match, logging the transition — so the
    // pipeline board reflects "submitted / awarded / declined" without a second manual step.
    private async Task MaybeAdvanceStageAsync(GrantApplication app, string submissionStatus, Guid userId, CancellationToken ct)
    {
        var target = submissionStatus switch
        {
            "submitted" or "under_review" or "more_info" => "Submitted",
            "awarded" => "Awarded",
            "declined" => "Declined",
            _ => null,
        };
        if (target is null || app.Stage == target) return;

        var from = app.Stage;
        app.Stage = target;
        db.GrantApplicationActivities.Add(new GrantApplicationActivity
        {
            ApplicationId = app.Id, OrgId = app.OrgId, ActorId = userId,
            Action = "submission", FromValue = from, ToValue = target,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string NormalizeSubmissionPortal(string portal)
    {
        var p = (portal ?? string.Empty).Trim().ToLowerInvariant();
        if (!SubmissionPortals.Contains(p))
            throw new ArgumentException($"Invalid portal '{portal}'. Allowed: {string.Join(", ", SubmissionPortals)}.", nameof(portal));
        return p;
    }

    private static string NormalizeSubmissionStatus(string status)
    {
        var s = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (!SubmissionStatuses.Contains(s))
            throw new ArgumentException($"Invalid status '{status}'. Allowed: {string.Join(", ", SubmissionStatuses)}.", nameof(status));
        return s;
    }

    private static GrantNarrativeSectionDto MapNarrative(GrantNarrativeSection s) =>
        new(s.Id, s.SectionKey, s.Title, s.Content, s.WordCount, s.TargetWords, s.Status, s.SortOrder, s.UpdatedAt);

    private static GrantSubmissionDto MapSubmission(GrantSubmission s, string? submittedByName) =>
        new(s.Id, s.Portal, s.Status, s.SubmittedAt, s.ConfirmationNumber, s.SubmittedBy, submittedByName,
            s.AmountAwarded, s.Notes, s.CreatedAt, s.UpdatedAt);
}
