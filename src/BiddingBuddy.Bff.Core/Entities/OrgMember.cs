namespace BiddingBuddy.Bff.Core.Entities;

public class OrgMember
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid UserId { get; set; }
    public string Role { get; set; } = "viewer";   // owner|admin|bid_manager|finance|sales|viewer
    public string? Department { get; set; }
    public string Status { get; set; } = "active";  // active|suspended
    public Guid? InvitedBy { get; set; }
    public DateTime? JoinedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public Organization Organization { get; set; } = default!;
    public User User { get; set; } = default!;
}
