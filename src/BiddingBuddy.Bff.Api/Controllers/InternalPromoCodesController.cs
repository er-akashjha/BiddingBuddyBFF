using BiddingBuddy.Bff.Api.Filters;
using BiddingBuddy.Bff.Core.Billing;
using BiddingBuddy.Bff.Core.DTOs.Billing;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Api.Controllers;

/// <summary>
/// Promo code administration. Operator-only via X-Api-Key, mirroring
/// <c>/internal/notification-templates</c> — there is no in-app admin console, and a
/// self-serve route to mint discounts would be a hole, not a feature.
///
/// Launch the founding-member offer with:
/// <code>
/// curl -X PATCH .../internal/promo-codes/FOUNDING40 -H "X-Api-Key: ..." \
///      -H "Content-Type: application/json" -d '{"isActive":true}'
/// </code>
/// </summary>
[ApiController]
[Route("internal/promo-codes")]
[PipelineApiKey]
[Produces("application/json")]
public class InternalPromoCodesController(BffDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PromoCodeAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await db.PromoCodes.AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => Map(p))
            .ToListAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(PromoCodeAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreatePromoCodeDto dto, CancellationToken ct)
    {
        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            return BadRequest(new { error = "Code is required.", code = "INVALID_CODE" });

        if (dto.DiscountType is not ("percent" or "flat"))
            return BadRequest(new { error = "discountType must be 'percent' or 'flat'.", code = "INVALID_DISCOUNT_TYPE" });

        if (dto.DiscountType == "percent" && dto.DiscountValue is < 1 or > 100)
            return BadRequest(new { error = "A percent discount must be between 1 and 100.", code = "INVALID_DISCOUNT_VALUE" });

        if (dto.DiscountType == "flat" && dto.DiscountValue <= 0)
            return BadRequest(new { error = "A flat discount must be a positive amount in paise.", code = "INVALID_DISCOUNT_VALUE" });

        // Unknown plan codes would silently make the code apply to nothing.
        var unknown = dto.AppliesToPlans?.Where(p => !PlanCatalog.ByCode.ContainsKey(p)).ToArray() ?? [];
        if (unknown.Length > 0)
            return BadRequest(new { error = $"Unknown plan code(s): {string.Join(", ", unknown)}.", code = "INVALID_PLAN" });

        if (await db.PromoCodes.AnyAsync(p => p.Code == code, ct))
            return Conflict(new { error = "That promo code already exists.", code = "CODE_EXISTS" });

        var promo = new PromoCode
        {
            Id             = Guid.NewGuid(),
            Code           = code,
            Description    = dto.Description,
            DiscountType   = dto.DiscountType,
            DiscountValue  = dto.DiscountValue,
            AppliesToPlans = dto.AppliesToPlans,
            MaxRedemptions = dto.MaxRedemptions,
            IsLifetime     = dto.IsLifetime,
            ValidFrom      = dto.ValidFrom,
            ValidUntil     = dto.ValidUntil,
            IsActive       = dto.IsActive,
            CreatedAt      = DateTime.UtcNow,
        };
        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, Map(promo));
    }

    /// <summary>
    /// Deactivate / extend / re-cap an existing code. The discount VALUE and type are
    /// deliberately immutable: orgs holding a lifetime redemption were promised that
    /// number, and rewriting it would silently change what they pay at renewal.
    /// </summary>
    [HttpPatch("{code}")]
    [ProducesResponseType(typeof(PromoCodeAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(string code, [FromBody] UpdatePromoCodeDto dto, CancellationToken ct)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Code == normalized, ct);
        if (promo is null) return NotFound(new { error = "Promo code not found." });

        if (dto.IsActive is { } active) promo.IsActive = active;
        if (dto.ValidUntil is { } until) promo.ValidUntil = until;
        if (dto.MaxRedemptions is { } max) promo.MaxRedemptions = max;
        if (dto.Description is not null) promo.Description = dto.Description;

        await db.SaveChangesAsync(ct);
        return Ok(Map(promo));
    }

    private static PromoCodeAdminDto Map(PromoCode p) => new(
        p.Id, p.Code, p.Description, p.DiscountType, p.DiscountValue, p.AppliesToPlans,
        p.MaxRedemptions, p.RedeemedCount, p.IsLifetime, p.ValidFrom, p.ValidUntil,
        p.IsActive, p.CreatedAt);
}
