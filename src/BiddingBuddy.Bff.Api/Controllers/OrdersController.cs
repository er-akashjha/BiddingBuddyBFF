using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Orders;
using BiddingBuddy.Bff.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiddingBuddy.Bff.Api.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
[Produces("application/json")]
public class OrdersController(IOrderService orderService) : BffControllerBase
{
    /// <summary>Paginated list of orders. Filter by status (received|processing|shipped|delivered|cancelled).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await orderService.ListAsync(CurrentOrgId, status, page, pageSize, ct);
        return Ok(result);
    }

    /// <summary>Get full order detail including line items and delivery milestones.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var order = await orderService.GetAsync(id, CurrentOrgId, ct);
        return Ok(order);
    }

    /// <summary>Create a new order, optionally linked to a bid or tender.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrderDto dto, CancellationToken ct)
    {
        var order = await orderService.CreateAsync(CurrentOrgId, dto, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Update order fields (status, buyer org, dates, value).</summary>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateOrderDto dto, CancellationToken ct)
    {
        var order = await orderService.UpdateAsync(id, CurrentOrgId, dto, ct);
        return Ok(order);
    }

    // ── Line Items ────────────────────────────────────────────────────────────

    /// <summary>Add a line item to an order.</summary>
    [HttpPost("{id:guid}/items")]
    [ProducesResponseType(typeof(OrderItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddItem(Guid id, [FromBody] CreateOrderItemDto dto, CancellationToken ct)
    {
        var item = await orderService.AddItemAsync(id, CurrentOrgId, dto, ct);
        return Ok(item);
    }

    /// <summary>Remove a line item from an order.</summary>
    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken ct)
    {
        await orderService.DeleteItemAsync(itemId, id, CurrentOrgId, ct);
        return NoContent();
    }

    // ── Milestones ────────────────────────────────────────────────────────────

    /// <summary>Add a delivery milestone to an order.</summary>
    [HttpPost("{id:guid}/milestones")]
    [ProducesResponseType(typeof(DeliveryMilestoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMilestone(Guid id, [FromBody] CreateMilestoneDto dto, CancellationToken ct)
    {
        var ms = await orderService.AddMilestoneAsync(id, CurrentOrgId, dto, ct);
        return Ok(ms);
    }

    /// <summary>Update a delivery milestone (title, due date, completion date, status, notes).</summary>
    [HttpPatch("{id:guid}/milestones/{milestoneId:guid}")]
    [ProducesResponseType(typeof(DeliveryMilestoneDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMilestone(Guid id, Guid milestoneId, [FromBody] UpdateMilestoneDto dto, CancellationToken ct)
    {
        var ms = await orderService.UpdateMilestoneAsync(milestoneId, id, CurrentOrgId, dto, ct);
        return Ok(ms);
    }

    /// <summary>Delete a delivery milestone.</summary>
    [HttpDelete("{id:guid}/milestones/{milestoneId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMilestone(Guid id, Guid milestoneId, CancellationToken ct)
    {
        await orderService.DeleteMilestoneAsync(milestoneId, id, CurrentOrgId, ct);
        return NoContent();
    }
}
