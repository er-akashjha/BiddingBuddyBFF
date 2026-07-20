using BiddingBuddy.Bff.Core.DTOs.Alerts;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class TenderAlertRuleService(BffDbContext db) : ITenderAlertRuleService
{
    public async Task<IReadOnlyList<TenderAlertRuleDto>> ListAsync(Guid orgId, CancellationToken ct = default)
        => await db.TenderAlertRules
            .Where(r => r.OrgId == orgId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => Map(r))
            .ToListAsync(ct);

    public async Task<TenderAlertRuleDto> CreateAsync(Guid orgId, Guid userId, CreateTenderAlertRuleDto dto, CancellationToken ct = default)
    {
        var rule = new TenderAlertRule
        {
            OrgId       = orgId,
            Name        = dto.Name,
            Categories  = Normalize(dto.Categories),
            States      = Normalize(dto.States),
            Keywords    = Normalize(dto.Keywords),
            MinValue    = dto.MinValue,
            MaxValue    = dto.MaxValue,
            MinAiScore  = dto.MinAiScore,
            IsActive    = dto.IsActive,
            CreatedBy   = userId,
        };
        db.TenderAlertRules.Add(rule);
        await db.SaveChangesAsync(ct);
        return Map(rule);
    }

    public async Task<TenderAlertRuleDto> UpdateAsync(Guid ruleId, Guid orgId, UpdateTenderAlertRuleDto dto, CancellationToken ct = default)
    {
        var rule = await db.TenderAlertRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Alert rule not found.");

        if (dto.Name is not null) rule.Name = dto.Name;
        if (dto.Categories is not null) rule.Categories = Normalize(dto.Categories);
        if (dto.States is not null) rule.States = Normalize(dto.States);
        if (dto.Keywords is not null) rule.Keywords = Normalize(dto.Keywords);
        if (dto.MinValue.HasValue) rule.MinValue = dto.MinValue;
        if (dto.MaxValue.HasValue) rule.MaxValue = dto.MaxValue;
        if (dto.MinAiScore.HasValue) rule.MinAiScore = dto.MinAiScore;
        if (dto.IsActive.HasValue) rule.IsActive = dto.IsActive.Value;

        await db.SaveChangesAsync(ct);
        return Map(rule);
    }

    public async Task DeleteAsync(Guid ruleId, Guid orgId, CancellationToken ct = default)
    {
        var rule = await db.TenderAlertRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Alert rule not found.");
        db.TenderAlertRules.Remove(rule);
        await db.SaveChangesAsync(ct);
    }

    public async Task<OrgAlertSettingsDto> GetSettingsAsync(Guid orgId, CancellationToken ct = default)
    {
        var s = await db.OrgAlertSettings.FirstOrDefaultAsync(x => x.OrgId == orgId, ct);
        return s is null
            ? new OrgAlertSettingsDto(true, 10, 360, ["Email", "InApp"], ["owner", "admin", "bid_manager"])
            : MapSettings(s);
    }

    public async Task<OrgAlertSettingsDto> UpdateSettingsAsync(Guid orgId, UpdateOrgAlertSettingsDto dto, CancellationToken ct = default)
    {
        // Validate up front: a rejected value must leave no half-applied row behind, and a
        // bad channel alongside a good role must persist neither.
        var channels = dto.NotifyChannels is null
            ? null : ValidateSet(dto.NotifyChannels, TenderDigestChannel.Supported, "notifyChannels");
        var roles = dto.NotifyRoles is null
            ? null : ValidateSet(dto.NotifyRoles, OrgAlertRole.All, "notifyRoles");

        var s = await db.OrgAlertSettings.FirstOrDefaultAsync(x => x.OrgId == orgId, ct);
        if (s is null)
        {
            s = new OrgAlertSettings { OrgId = orgId };
            db.OrgAlertSettings.Add(s);
        }

        if (dto.IsEnabled.HasValue) s.IsEnabled = dto.IsEnabled.Value;
        if (dto.DigestSize.HasValue) s.DigestSize = Math.Clamp(dto.DigestSize.Value, 1, 50);
        if (dto.MinSendIntervalMinutes.HasValue) s.MinSendIntervalMinutes = Math.Clamp(dto.MinSendIntervalMinutes.Value, 15, 10080);
        if (channels is not null) s.NotifyChannels = channels;
        if (roles is not null) s.NotifyRoles = roles;

        await db.SaveChangesAsync(ct);
        return MapSettings(s);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Drop blanks; null-out empty arrays so an empty constraint is treated as "ignore".
    private static string[]? Normalize(string[]? values)
    {
        if (values is null) return null;
        var cleaned = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>
    /// Check a settings array against the values the digest path actually honours, returning
    /// the canonical spelling of each. Unlike <see cref="Normalize"/>, empty is an error rather
    /// than "ignore": these arrays are the recipient list, so an empty one means "deliver
    /// nowhere" — the exact silent failure this guard exists to stop.
    /// </summary>
    private static string[] ValidateSet(string[] values, string[] allowed, string field)
    {
        var cleaned = new List<string>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();

            // Match case-insensitively but store the canonical constant: dispatch compares
            // ordinally, so "email" would otherwise be saved happily and then match nothing.
            var canonical = allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    $"{field}: '{value}' is not supported. Allowed values: {string.Join(", ", allowed)}.", field);

            if (!cleaned.Contains(canonical)) cleaned.Add(canonical);
        }

        if (cleaned.Count == 0)
            throw new ArgumentException(
                $"{field} must contain at least one of: {string.Join(", ", allowed)}. " +
                "To stop digests entirely, set isEnabled to false.", field);

        return [.. cleaned];
    }

    private static TenderAlertRuleDto Map(TenderAlertRule r) => new(
        r.Id, r.Name, r.Categories, r.States, r.Keywords,
        r.MinValue, r.MaxValue, r.MinAiScore, r.IsActive, r.CreatedAt, r.UpdatedAt);

    private static OrgAlertSettingsDto MapSettings(OrgAlertSettings s) => new(
        s.IsEnabled, s.DigestSize, s.MinSendIntervalMinutes, s.NotifyChannels, s.NotifyRoles);
}
