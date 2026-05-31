using BiddingBuddy.Bff.Core.DTOs.Orders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController(IOrderService orderService) : BffControllerBase
{
    /// <summary>GET /api/orders?status=&amp;page=1&amp;pageSize=20</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await orderService.ListAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>GET /api/orders/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var order = await orderService.GetAsync(id, CurrentOrgId, ct);
        return Ok(order);
    }

    /// <summary>POST /api/orders</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var order = await orderService.CreateAsync(CurrentOrgId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>PATCH /api/orders/{id}</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrderDto dto, CancellationToken ct)
    {
        var order = await orderService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(order);
    }

    // ── Items ─────────────────────────────────────────────────────────────────

    /// <summary>POST /api/orders/{id}/items</summary>
    [HttpPost("{id:guid}/items")]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] CreateOrderItemDto dto, CancellationToken ct)
    {
        var item = await orderService.AddItemAsync(id, CurrentOrgId, dto, ct);
        return Ok(item);
    }

    /// <summary>DELETE /api/orders/{id}/items/{itemId}</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await orderService.DeleteItemAsync(itemId, id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Milestones ────────────────────────────────────────────────────────────

    /// <summary>POST /api/orders/{id}/milestones</summary>
    [HttpPost("{id:guid}/milestones")]
    public async Task<IActionResult> AddMilestone(Guid id, [FromBody] CreateMilestoneDto dto, CancellationToken ct)
    {
        var ms = await orderService.AddMilestoneAsync(id, CurrentOrgId, dto, ct);
        return Ok(ms);
    }

    /// <summary>PATCH /api/orders/{id}/milestones/{milestoneId}</summary>
    [HttpPatch("{id:guid}/milestones/{milestoneId:guid}")]
    public async Task<IActionResult> UpdateMilestone(Guid id, Guid milestoneId, [FromBody] UpdateMilestoneDto dto, CancellationToken ct)
    {
        var ms = await orderService.UpdateMilestoneAsync(milestoneId, id, CurrentOrgId, dto, ct);
        return Ok(ms);
    }

    /// <summary>DELETE /api/orders/{id}/milestones/{milestoneId}</summary>
    [HttpDelete("{id:guid}/milestones/{milestoneId:guid}")]
    public async Task<IActionResult> DeleteMilestone(Guid id, Guid milestoneId, CancellationToken ct)
    {
        await orderService.DeleteMilestoneAsync(milestoneId, id, CurrentOrgId, ct);
        return NoContent();
    }
}
