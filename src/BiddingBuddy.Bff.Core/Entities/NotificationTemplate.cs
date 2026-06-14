namespace BiddingBuddy.Bff.Core.Entities;

/// <summary>
/// Handlebars template for rendering a notification on a specific channel.
/// One row per (code, channel) — e.g. ("WELCOME","Email"), ("WELCOME","InApp").
/// Maps to <c>notification_templates</c>.
/// </summary>
public class NotificationTemplate
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;            // logical event, e.g. WELCOME
    public string Channel { get; set; } = default!;         // Email | Sms | WhatsApp | Firebase | InApp
    public string Name { get; set; } = default!;
    public string? Subject { get; set; }                    // Handlebars; email subject / push & in-app title
    public string Body { get; set; } = default!;            // Handlebars
    public string BodyFormat { get; set; } = "Html";        // Html | Text | Markdown
    public string Metadata { get; set; } = "{}";            // jsonb — channel-specific extras (Handlebars-templated strings)
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
