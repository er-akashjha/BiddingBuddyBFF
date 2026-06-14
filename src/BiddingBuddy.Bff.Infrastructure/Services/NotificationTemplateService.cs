using BiddingBuddy.Bff.Core.DTOs.Notifications;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class NotificationTemplateService(BffDbContext db) : INotificationTemplateService
{
    private static readonly HashSet<string> AllowedChannels = new(StringComparer.Ordinal)
    {
        NotificationChannel.Email, NotificationChannel.Sms, NotificationChannel.WhatsApp,
        NotificationChannel.Firebase, NotificationChannel.InApp,
    };
    private static readonly HashSet<string> AllowedFormats = new(StringComparer.Ordinal)
    {
        "Html", "Text", "Markdown",
    };

    public async Task<IReadOnlyList<NotificationTemplateDto>> ListAsync(string? code, string? channel, CancellationToken ct = default)
    {
        var q = db.NotificationTemplates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(code))    q = q.Where(t => t.Code == code);
        if (!string.IsNullOrWhiteSpace(channel)) q = q.Where(t => t.Channel == channel);

        return await q.OrderBy(t => t.Code).ThenBy(t => t.Channel)
            .Select(t => Map(t))
            .ToListAsync(ct);
    }

    public async Task<NotificationTemplateDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Notification template not found.");
        return Map(t);
    }

    public async Task<NotificationTemplateDto> CreateAsync(CreateNotificationTemplateDto dto, CancellationToken ct = default)
    {
        ValidateChannel(dto.Channel);
        ValidateFormat(dto.BodyFormat);

        var template = new NotificationTemplate
        {
            Id         = Guid.NewGuid(),
            Code       = dto.Code,
            Channel    = dto.Channel,
            Name       = dto.Name,
            Subject    = dto.Subject,
            Body       = dto.Body,
            BodyFormat = string.IsNullOrWhiteSpace(dto.BodyFormat) ? "Html" : dto.BodyFormat,
            Metadata   = string.IsNullOrWhiteSpace(dto.Metadata) ? "{}" : dto.Metadata,
            IsActive   = dto.IsActive,
            CreatedAt  = DateTime.UtcNow,
        };
        db.NotificationTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Map(template);
    }

    public async Task<NotificationTemplateDto> UpdateAsync(Guid id, UpdateNotificationTemplateDto dto, CancellationToken ct = default)
    {
        var t = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Notification template not found.");

        if (dto.Name is not null)       t.Name       = dto.Name;
        if (dto.Subject is not null)    t.Subject    = dto.Subject;
        if (dto.Body is not null)       t.Body       = dto.Body;
        if (dto.BodyFormat is not null) { ValidateFormat(dto.BodyFormat); t.BodyFormat = dto.BodyFormat; }
        if (dto.Metadata is not null)   t.Metadata   = dto.Metadata;
        if (dto.IsActive.HasValue)      t.IsActive   = dto.IsActive.Value;

        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(t);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.NotificationTemplates.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Notification template not found.");
        db.NotificationTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    private static NotificationTemplateDto Map(NotificationTemplate t) => new(
        t.Id, t.Code, t.Channel, t.Name, t.Subject, t.Body, t.BodyFormat,
        t.Metadata, t.IsActive, t.CreatedAt, t.UpdatedAt);

    private static void ValidateChannel(string ch)
    {
        if (!AllowedChannels.Contains(ch))
            throw new ArgumentException(
                $"Channel must be one of: {string.Join(", ", AllowedChannels)} (case-sensitive). Got: '{ch}'.");
    }

    private static void ValidateFormat(string fmt)
    {
        if (!AllowedFormats.Contains(fmt))
            throw new ArgumentException(
                $"BodyFormat must be one of: {string.Join(", ", AllowedFormats)} (case-sensitive). Got: '{fmt}'.");
    }
}
