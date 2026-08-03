namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>A task on a grant application's plan checklist (the detail-page "Plan" tab).</summary>
public class GrantApplicationChecklistItem
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public string Title { get; set; } = default!;
    public bool IsDone { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid? AssignedTo { get; set; }
    public int SortOrder { get; set; }
    public DateTime? DoneAt { get; set; }
    public Guid? DoneBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
}
