using System.Globalization;
using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Trial and renewal lifecycle. Every email is deduped by its own stamp column, so the
/// scan can run as often as it likes without spamming anyone.
///
/// <para><b>This worker does not grant or revoke access.</b> PlanService resolves
/// entitlements from the dates on the row, so a stalled or crashed worker leaves people
/// correctly downgraded rather than silently subscribed. What it changes is the status
/// LABEL (what the UI shows) and what gets emailed.</para>
/// </summary>
public class SubscriptionLifecycleService(
    BffDbContext db,
    IPlanService planService,
    INotificationPublisher notifications,
    INotificationAudienceResolver audience,
    IConfiguration config,
    ILogger<SubscriptionLifecycleService> log) : ISubscriptionLifecycleService
{
    private static readonly string[] BillingRoles = ["owner", "admin"];

    private string BillingUrl =>
        $"{(config["Frontend:BaseUrl"] ?? "http://localhost:3000").TrimEnd('/')}/billing";

    public async Task<SubscriptionLifecycleResult> RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        int trialEnding = 0, trialsExpired = 0, renewals = 0, pastDue = 0, expired = 0;

        var subs = await db.OrgSubscriptions
            .Where(s => s.Status == "trialing" || s.Status == "active" || s.Status == "past_due")
            .ToListAsync(ct);

        foreach (var sub in subs)
        {
            if (ct.IsCancellationRequested) break;

            switch (sub.Status)
            {
                case "trialing" when sub.TrialEndsAt is { } trialEnd:
                    if (trialEnd <= now)
                    {
                        sub.Status = "expired";
                        sub.ExpiryNotifiedAt = now;
                        planService.Invalidate(sub.OrgId);
                        expired++;
                        trialsExpired++;
                    }
                    else if (trialEnd - now <= TimeSpan.FromDays(3) && sub.TrialEndingNotifiedAt is null)
                    {
                        await NotifyAsync(sub, "TRIAL_ENDING", DaysBetween(now, trialEnd), trialEnd, ct);
                        sub.TrialEndingNotifiedAt = now;
                        trialEnding++;
                    }
                    break;

                case "active" when sub.CurrentPeriodEnd is { } periodEnd:
                    if (periodEnd < now)
                    {
                        // Paid access continues through the grace window (PlanResolution.Grace);
                        // this is the label change plus the last-chance nudge.
                        sub.Status = "past_due";
                        planService.Invalidate(sub.OrgId);
                        pastDue++;
                    }
                    else if (periodEnd - now <= TimeSpan.FromDays(3) && sub.RenewalT3NotifiedAt is null)
                    {
                        await NotifyAsync(sub, "RENEWAL_REMINDER", DaysBetween(now, periodEnd), periodEnd, ct);
                        sub.RenewalT3NotifiedAt = now;
                        renewals++;
                    }
                    else if (periodEnd - now <= TimeSpan.FromDays(14) && sub.RenewalT14NotifiedAt is null)
                    {
                        await NotifyAsync(sub, "RENEWAL_REMINDER", DaysBetween(now, periodEnd), periodEnd, ct);
                        sub.RenewalT14NotifiedAt = now;
                        renewals++;
                    }
                    break;

                case "past_due" when sub.CurrentPeriodEnd is { } graceEnd:
                    if (now > graceEnd + PlanResolution.Grace)
                    {
                        sub.Status = "expired";
                        sub.ExpiryNotifiedAt ??= now;
                        planService.Invalidate(sub.OrgId);
                        expired++;
                    }
                    break;
            }
        }

        await db.SaveChangesAsync(ct);

        if (trialEnding + trialsExpired + renewals + pastDue + expired > 0)
            log.LogInformation(
                "[Subscriptions] {TrialEnding} trial-ending, {TrialsExpired} trials expired, {Renewals} renewal reminder(s), {PastDue} past-due, {Expired} expired.",
                trialEnding, trialsExpired, renewals, pastDue, expired);

        return new SubscriptionLifecycleResult(trialEnding, trialsExpired, renewals, pastDue, expired);
    }

    private async Task NotifyAsync(
        OrgSubscription sub, string templateCode, int daysLeft, DateTime deadline, CancellationToken ct)
    {
        try
        {
            var org = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == sub.OrgId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct) ?? "your organization";

            var recipients = await audience.ByRolesAsync(sub.OrgId, BillingRoles, ct: ct);
            var planName = PlanCatalog.Get(sub.PlanCode).Name;
            var deadlineText = deadline.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

            foreach (var member in recipients)
            {
                if (member.Email is null) continue;

                await notifications.SendAsync(new SendNotificationDto(
                    Category:     NotificationCategory.Transactional,
                    TemplateCode: templateCode,
                    UserId:       member.UserId,
                    Payload: new Dictionary<string, object>
                    {
                        ["FirstName"]        = FirstName(member.Name),
                        ["OrganizationName"] = org,
                        ["PlanName"]         = planName,
                        ["DaysLeft"]         = daysLeft,
                        ["TrialEndsAt"]      = deadlineText,
                        ["PeriodEnd"]        = deadlineText,
                        ["BillingUrl"]       = BillingUrl,
                    },
                    Recipients: new[]
                    {
                        new NotificationRecipientDto(NotificationChannel.Email, member.Email),
                        new NotificationRecipientDto(NotificationChannel.InApp, member.UserId.ToString()),
                    }), ct);
            }
        }
        catch (Exception ex)
        {
            // Stamped by the caller regardless: a permanently-failing notification must
            // not make every subsequent scan retry it forever.
            log.LogWarning(ex, "[Subscriptions] {Template} could not be published for org {OrgId}",
                templateCode, sub.OrgId);
        }
    }

    private static int DaysBetween(DateTime from, DateTime to)
        => Math.Max(0, (int)Math.Ceiling((to - from).TotalDays));

    private static string FirstName(string? name)
        => string.IsNullOrWhiteSpace(name) ? "there" : name.Split(' ')[0];
}
