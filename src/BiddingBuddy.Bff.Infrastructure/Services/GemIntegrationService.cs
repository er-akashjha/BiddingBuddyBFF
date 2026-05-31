using BiddingBuddy.Bff.Core.DTOs.Integrations;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class GemIntegrationService(BffDbContext db) : IGemIntegrationService
{
    public async Task<GemIntegrationDto?> GetAsync(Guid orgId, CancellationToken ct = default)
    {
        var integration = await db.GemIntegrations
            .FirstOrDefaultAsync(g => g.OrgId == orgId, ct);
        return integration is null ? null : MapDto(integration);
    }

    public async Task<GemIntegrationDto> UpsertAsync(Guid orgId, UpsertGemIntegrationDto dto, CancellationToken ct = default)
    {
        var integration = await db.GemIntegrations
            .FirstOrDefaultAsync(g => g.OrgId == orgId, ct);

        if (integration is null)
        {
            integration = new GemIntegration
            {
                OrgId       = orgId,
                GemSellerId = dto.GemSellerId,
                GemUsername = dto.GemUsername,
                SyncEnabled = dto.SyncEnabled,
                Preferences = dto.Preferences,
                SyncStatus  = "idle",
            };
            db.GemIntegrations.Add(integration);
        }
        else
        {
            integration.GemSellerId = dto.GemSellerId;
            integration.GemUsername = dto.GemUsername;
            integration.SyncEnabled = dto.SyncEnabled;
            if (dto.Preferences is not null) integration.Preferences = dto.Preferences;
        }

        await db.SaveChangesAsync(ct);
        return MapDto(integration);
    }

    private static GemIntegrationDto MapDto(GemIntegration g)
        => new(g.Id, g.GemSellerId, g.GemUsername, g.SyncEnabled,
            g.LastSyncedAt, g.SyncStatus, g.SyncError, g.Preferences, g.UpdatedAt);
}
