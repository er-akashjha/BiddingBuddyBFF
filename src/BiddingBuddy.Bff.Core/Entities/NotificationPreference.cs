namespace BiddingBuddy.Bff.Core.Entities;

public class NotificationPreference
{
    public Guid UserId { get; set; }
    public Guid OrgId { get; set; }
    public string Channel { get; set; } = "in_app";  // in_app|email|whatsapp
    public string[] EventTypes { get; set; } = ["tender_closing", "bid_due", "emd_due"];
    public bool IsEnabled { get; set; } = true;

    public User User { get; set; } = default!;
    public Organization Organization { get; set; } = default!;
}
