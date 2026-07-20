using BiddingBuddy.Bff.Core.DTOs.Alerts;
using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using BiddingBuddy.Bff.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BiddingBuddy.Bff.Tests.Alerts;

/// <summary>
/// org_alert_settings.notify_channels / notify_roles are bare text[] with no CHECK, and
/// MatchingService.DispatchDigestAsync consumes both by exact match — it only ever builds
/// recipients for Email and InApp, and resolves members with roles.Contains(member.Role).
/// So a value neither side implements doesn't error, it just yields no recipients and the
/// org's digests stop dead with nothing to show for it. These tests pin the save-time guard
/// that makes that state unreachable.
/// </summary>
public sealed class OrgAlertSettingsValidationTests
{
    private static readonly Guid Org = Guid.NewGuid();

    private static BffDbContext Db(string name) =>
        new(new DbContextOptionsBuilder<BffDbContext>().UseInMemoryDatabase(name).Options);

    private static UpdateOrgAlertSettingsDto Channels(params string[] channels) =>
        new(null, null, null, channels, null);

    private static UpdateOrgAlertSettingsDto Roles(params string[] roles) =>
        new(null, null, null, null, roles);

    // ── Channels the dispatch path actually implements ───────────────────────

    [Fact]
    public async Task Channels_Supported_AreSaved()
    {
        using var db = Db(nameof(Channels_Supported_AreSaved));

        var settings = await new TenderAlertRuleService(db)
            .UpdateSettingsAsync(Org, Channels("Email", "InApp"), default);

        Assert.Equal(["Email", "InApp"], settings.NotifyChannels);
        Assert.Equal(["Email", "InApp"], (await db.OrgAlertSettings.SingleAsync()).NotifyChannels);
    }

    // Sms/WhatsApp/Firebase are real NotificationChannel constants — they look legitimate,
    // which is exactly why validating against that class alone would not close this hole.
    [Theory]
    [InlineData("WhatsApp")]
    [InlineData("Sms")]
    [InlineData("Firebase")]
    [InlineData("Telegram")]
    public async Task Channels_RealConstantsDispatchIgnores_AreRejected_AndPersistNothing(string channel)
    {
        using var db = Db($"{nameof(Channels_RealConstantsDispatchIgnores_AreRejected_AndPersistNothing)}_{channel}");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Channels(channel), default));

        Assert.Contains(channel, ex.Message);
        Assert.Empty(await db.OrgAlertSettings.ToListAsync());
    }

    [Fact]
    public async Task Channels_MixOfSupportedAndUnsupported_IsRejectedWholesale()
    {
        using var db = Db(nameof(Channels_MixOfSupportedAndUnsupported_IsRejectedWholesale));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Channels("Email", "WhatsApp"), default));

        Assert.Empty(await db.OrgAlertSettings.ToListAsync());
    }

    // The constants are compared ordinally at dispatch, so "email" would save fine and match
    // nothing. Accept the caller's casing but store the canonical spelling.
    [Theory]
    [InlineData("email", "Email")]
    [InlineData("EMAIL", "Email")]
    [InlineData("  Email  ", "Email")]
    [InlineData("inapp", "InApp")]
    public async Task Channels_AreCanonicalised(string input, string stored)
    {
        using var db = Db($"{nameof(Channels_AreCanonicalised)}_{input.Trim()}");

        var settings = await new TenderAlertRuleService(db)
            .UpdateSettingsAsync(Org, Channels(input), default);

        Assert.Equal([stored], settings.NotifyChannels);
    }

    [Fact]
    public async Task Channels_Duplicates_AreCollapsed()
    {
        using var db = Db(nameof(Channels_Duplicates_AreCollapsed));

        var settings = await new TenderAlertRuleService(db)
            .UpdateSettingsAsync(Org, Channels("Email", "email", "EMAIL"), default);

        Assert.Equal(["Email"], settings.NotifyChannels);
    }

    // Empty is the one the SPA can reach today: both switches off saves [], which dispatch
    // turns into zero recipients. isEnabled=false is the real "stop sending" control.
    [Fact]
    public async Task Channels_Empty_IsRejected()
    {
        using var db = Db(nameof(Channels_Empty_IsRejected));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Channels(), default));

        Assert.Contains("isEnabled", ex.Message);
        Assert.Empty(await db.OrgAlertSettings.ToListAsync());
    }

    [Fact]
    public async Task Channels_OnlyBlanks_IsRejected()
    {
        using var db = Db(nameof(Channels_OnlyBlanks_IsRejected));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Channels("", "   "), default));
    }

    // ── Roles (same failure mode via roles.Contains(member.Role)) ────────────

    [Fact]
    public async Task Roles_Valid_AreSaved()
    {
        using var db = Db(nameof(Roles_Valid_AreSaved));

        var settings = await new TenderAlertRuleService(db)
            .UpdateSettingsAsync(Org, Roles("owner", "finance", "viewer"), default);

        Assert.Equal(["owner", "finance", "viewer"], settings.NotifyRoles);
    }

    [Theory]
    [InlineData("bidmanager")]   // missing underscore
    [InlineData("bid manager")]  // space for underscore
    [InlineData("superadmin")]   // not a role at all
    public async Task Roles_Unknown_AreRejected_AndPersistNothing(string role)
    {
        using var db = Db($"{nameof(Roles_Unknown_AreRejected_AndPersistNothing)}_{role}");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Roles(role), default));

        Assert.Empty(await db.OrgAlertSettings.ToListAsync());
    }

    // organization_members.role is lowercase, so "Owner" would match no member. It's a casing
    // slip rather than a different role — canonicalise it the way channels are canonicalised.
    [Theory]
    [InlineData("Owner", "owner")]
    [InlineData("BID_MANAGER", "bid_manager")]
    public async Task Roles_AreCanonicalised(string input, string stored)
    {
        using var db = Db($"{nameof(Roles_AreCanonicalised)}_{input}");

        var settings = await new TenderAlertRuleService(db)
            .UpdateSettingsAsync(Org, Roles(input), default);

        Assert.Equal([stored], settings.NotifyRoles);
    }

    [Fact]
    public async Task Roles_Empty_IsRejected()
    {
        using var db = Db(nameof(Roles_Empty_IsRejected));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(Org, Roles(), default));
    }

    // ── Interaction with the rest of the settings row ────────────────────────

    [Fact]
    public async Task BadChannel_DoesNotApplyTheOtherFieldsInTheSameCall()
    {
        using var db = Db(nameof(BadChannel_DoesNotApplyTheOtherFieldsInTheSameCall));
        db.OrgAlertSettings.Add(new OrgAlertSettings { OrgId = Org, DigestSize = 10, IsEnabled = true });
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new TenderAlertRuleService(db).UpdateSettingsAsync(
                Org, new UpdateOrgAlertSettingsDto(false, 25, null, ["WhatsApp"], ["owner"]), default));

        db.ChangeTracker.Clear();
        var row = await db.OrgAlertSettings.SingleAsync();
        Assert.True(row.IsEnabled);      // not flipped by the rejected call
        Assert.Equal(10, row.DigestSize);
    }

    [Fact]
    public async Task NullArrays_LeaveExistingValuesAlone()
    {
        using var db = Db(nameof(NullArrays_LeaveExistingValuesAlone));
        db.OrgAlertSettings.Add(new OrgAlertSettings
        {
            OrgId = Org, NotifyChannels = ["Email"], NotifyRoles = ["owner"],
        });
        await db.SaveChangesAsync();

        var settings = await new TenderAlertRuleService(db).UpdateSettingsAsync(
            Org, new UpdateOrgAlertSettingsDto(null, null, 720, null, null), default);

        Assert.Equal(["Email"], settings.NotifyChannels);
        Assert.Equal(["owner"], settings.NotifyRoles);
        Assert.Equal(720, settings.MinSendIntervalMinutes);
    }

    /// <summary>
    /// Tripwire, not a tautology. The allowlist is only correct while it matches the branches in
    /// DispatchDigestAsync — which no unit test can assert directly. Adding a channel to
    /// Supported without wiring dispatch would re-open the silent drop, so this fails to force
    /// that pairing into the same change. If you're here after a deliberate addition, add the
    /// dispatch branch, then update this.
    /// </summary>
    [Fact]
    public void SupportedChannels_MatchWhatDispatchImplements()
    {
        Assert.Equal([NotificationChannel.Email, NotificationChannel.InApp], TenderDigestChannel.Supported);
    }

    /// <summary>A typo in Supported would be its own silent drop — it would pass validation and
    /// then match no dispatch branch. Pin the entries to the real channel constants.</summary>
    [Fact]
    public void SupportedChannels_AreRealNotificationChannelConstants()
    {
        string[] known =
        [
            NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.WhatsApp,
            NotificationChannel.Firebase, NotificationChannel.InApp,
        ];

        Assert.All(TenderDigestChannel.Supported, c => Assert.Contains(c, known));
    }
}
