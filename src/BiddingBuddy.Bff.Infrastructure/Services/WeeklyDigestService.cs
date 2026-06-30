using System.Globalization;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Computes the weekly summary per org from live data (no snapshot dependency) and sends it to
/// owners + admins. A per-(org, ISO-week) claim in notification_reminders guarantees one digest
/// per org per week even though the worker ticks several times a day. Orgs with no open bids and
/// no recent wins are skipped so we never email an empty digest.
/// </summary>
public class WeeklyDigestService(
    BffDbContext db,
    INotificationPublisher publisher,
    INotificationAudienceResolver audience,
    IConfiguration config,
    ILogger<WeeklyDigestService> log) : IWeeklyDigestService
{
    private static readonly string[] DigestRoles = ["owner", "admin"];

    private string FrontendBaseUrl => (config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now        = DateTime.UtcNow;
        var today      = DateOnly.FromDateTime(now);
        var weekAhead  = today.AddDays(7);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var weekKey    = $"WEEKLY:{ISOWeek.GetYear(now)}-W{ISOWeek.GetWeekOfYear(now):00}";

        var orgIds = await db.Organizations.AsNoTracking()
            .Where(o => o.IsActive)
            .Select(o => o.Id)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var orgId in orgIds)
        {
            // Once per ISO week per org.
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO notification_reminders (org_id, entity_type, entity_id, reminder_key)
                VALUES ({orgId}, {"org_digest"}, {orgId}, {weekKey})
                ON CONFLICT (entity_type, entity_id, reminder_key) DO NOTHING", ct);
            if (claimed != 1) continue;

            var openBids     = await db.Bids.AsNoTracking().CountAsync(b => b.OrgId == orgId && b.StatusCategory == "open", ct);
            var wonThisMonth = await db.Bids.AsNoTracking().CountAsync(b => b.OrgId == orgId && b.Stage == "won" && b.UpdatedAt >= monthStart, ct);

            // Don't email a digest to a dormant org.
            if (openBids == 0 && wonThisMonth == 0) continue;

            var dueThisWeek = await db.Bids.AsNoTracking().CountAsync(b =>
                b.OrgId == orgId && b.StatusCategory == "open" && b.DueDate != null && b.DueDate >= today && b.DueDate <= weekAhead, ct);
            var overdue = await db.Bids.AsNoTracking().CountAsync(b =>
                b.OrgId == orgId && b.StatusCategory == "open" && b.DueDate != null && b.DueDate < today, ct);

            var recipients = await audience.ByRolesAsync(orgId, DigestRoles, null, ct);
            if (recipients.Count == 0) continue;

            var orgName = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "your organization";

            foreach (var m in recipients)
            {
                var channels = new List<NotificationRecipientDto>
                {
                    new(NotificationChannel.InApp, m.UserId.ToString()),
                };
                if (!string.IsNullOrWhiteSpace(m.Email))
                    channels.Add(new NotificationRecipientDto(NotificationChannel.Email, m.Email!));

                try
                {
                    await publisher.SendAsync(new SendNotificationDto(
                        Category:     NotificationCategory.Marketing,
                        TemplateCode: "ORG_WEEKLY_DIGEST",
                        UserId:       m.UserId,
                        Payload: new Dictionary<string, object>
                        {
                            ["FirstName"]    = FirstNameOf(m.Name),
                            ["OrgName"]      = orgName,
                            ["OpenBids"]     = openBids,
                            ["DueThisWeek"]  = dueThisWeek,
                            ["OverdueBids"]  = overdue,
                            ["WonThisMonth"] = wonThisMonth,
                            ["OrgId"]        = orgId.ToString(),
                            ["Link"]         = $"{FrontendBaseUrl}/dashboard",
                        },
                        Recipients: channels), ct);
                    sent++;
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "[WeeklyDigest] Failed to send digest to user {UserId}", m.UserId);
                }
            }
        }

        if (sent > 0) log.LogInformation("[WeeklyDigest] Sent {Sent} weekly digest notification(s).", sent);
        return sent;
    }

    private static string FirstNameOf(string? fullName)
        => string.IsNullOrWhiteSpace(fullName) ? "there" : fullName.Trim().Split(' ')[0];
}
