namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>One entry in a grant application's activity feed (creation, stage change, note, …).</summary>
public class GrantApplicationActivity
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid OrgId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = default!;
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public GrantApplication Application { get; set; } = default!;
    public User? Actor { get; set; }
}
