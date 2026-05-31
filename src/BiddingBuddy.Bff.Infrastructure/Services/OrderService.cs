using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Orders;
using BiddingBuddy.Bff.Core.Entities;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BiddingBuddy.Bff.Infrastructure.Services;

public class OrderService(BffDbContext db) : IOrderService
{
    public async Task<PagedResult<OrderListItemDto>> ListAsync(
        Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Orders.Where(o => o.OrgId == orgId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(o => o.Status == status);

        var total = await query.CountAsync(ct);
        var pg    = Math.Max(1, page);
        var sz    = Math.Clamp(pageSize, 1, 100);

        var orders = await query
            .Include(o => o.Bid)
            .OrderByDescending(o => o.UpdatedAt)
            .Skip((pg - 1) * sz)
            .Take(sz)
            .ToListAsync(ct);

        var items = orders.Select(o => new OrderListItemDto(
            o.Id, o.GemOrderId, o.OrderNumber, o.BuyerOrg,
            o.OrderDate, o.DeliveryDate, o.TotalValue, o.Status,
            o.BidId, o.Bid?.Title, o.UpdatedAt)).ToList();

        return new PagedResult<OrderListItemDto>(items, total, pg, sz);
    }

    public async Task<OrderDetailDto> GetAsync(Guid orderId, Guid orgId, CancellationToken ct = default)
    {
        var order = await db.Orders
            .Include(o => o.Bid)
            .Include(o => o.Tender)
            .Include(o => o.Items)
            .Include(o => o.Milestones)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        return MapToDetail(order);
    }

    public async Task<OrderDetailDto> CreateAsync(Guid orgId, CreateOrderDto dto, CancellationToken ct = default)
    {
        var order = new Order
        {
            OrgId        = orgId,
            BidId        = dto.BidId,
            TenderId     = dto.TenderId,
            GemOrderId   = dto.GemOrderId,
            OrderNumber  = dto.OrderNumber,
            BuyerOrg     = dto.BuyerOrg,
            OrderDate    = dto.OrderDate,
            DeliveryDate = dto.DeliveryDate,
            TotalValue   = dto.TotalValue,
            Status       = "received",
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return await GetAsync(order.Id, orgId, ct);
    }

    public async Task<OrderDetailDto> UpdateAsync(Guid orderId, Guid orgId, UpdateOrderDto dto, CancellationToken ct = default)
    {
        var order = await db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Order not found.");

        if (dto.GemOrderId   is not null) order.GemOrderId   = dto.GemOrderId;
        if (dto.OrderNumber  is not null) order.OrderNumber   = dto.OrderNumber;
        if (dto.BuyerOrg     is not null) order.BuyerOrg     = dto.BuyerOrg;
        if (dto.OrderDate.HasValue)       order.OrderDate     = dto.OrderDate;
        if (dto.DeliveryDate.HasValue)    order.DeliveryDate  = dto.DeliveryDate;
        if (dto.TotalValue.HasValue)      order.TotalValue    = dto.TotalValue;
        if (dto.Status       is not null) order.Status        = dto.Status;

        await db.SaveChangesAsync(ct);
        return await GetAsync(orderId, orgId, ct);
    }

    public async Task<OrderItemDto> AddItemAsync(Guid orderId, Guid orgId, CreateOrderItemDto dto, CancellationToken ct = default)
    {
        var exists = await db.Orders.AnyAsync(o => o.Id == orderId && o.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Order not found.");

        var item = new OrderItem
        {
            OrderId    = orderId,
            OrgId      = orgId,
            Description = dto.Description,
            Quantity   = dto.Quantity,
            UnitPrice  = dto.UnitPrice,
            TotalPrice = dto.UnitPrice * dto.Quantity,
            HsnCode    = dto.HsnCode,
        };
        db.OrderItems.Add(item);
        await db.SaveChangesAsync(ct);
        return new OrderItemDto(item.Id, item.Description, item.Quantity, item.UnitPrice, item.TotalPrice, item.HsnCode, item.CreatedAt);
    }

    public async Task DeleteItemAsync(Guid itemId, Guid orderId, Guid orgId, CancellationToken ct = default)
    {
        var item = await db.OrderItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.OrderId == orderId && i.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Order item not found.");
        db.OrderItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliveryMilestoneDto> AddMilestoneAsync(Guid orderId, Guid orgId, CreateMilestoneDto dto, CancellationToken ct = default)
    {
        var exists = await db.Orders.AnyAsync(o => o.Id == orderId && o.OrgId == orgId, ct);
        if (!exists) throw new KeyNotFoundException("Order not found.");

        var ms = new DeliveryMilestone
        {
            OrderId = orderId,
            OrgId   = orgId,
            Title   = dto.Title,
            DueDate = dto.DueDate,
            Notes   = dto.Notes,
        };
        db.DeliveryMilestones.Add(ms);
        await db.SaveChangesAsync(ct);
        return new DeliveryMilestoneDto(ms.Id, ms.Title, ms.DueDate, ms.CompletedAt, ms.Notes, ms.CreatedAt);
    }

    public async Task<DeliveryMilestoneDto> UpdateMilestoneAsync(Guid milestoneId, Guid orderId, Guid orgId, UpdateMilestoneDto dto, CancellationToken ct = default)
    {
        var ms = await db.DeliveryMilestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.OrderId == orderId && m.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Milestone not found.");

        if (dto.Title is not null)            ms.Title       = dto.Title;
        if (dto.DueDate.HasValue)             ms.DueDate     = dto.DueDate;
        if (dto.Notes is not null)            ms.Notes       = dto.Notes;
        if (dto.CompletedDate.HasValue)       ms.CompletedAt = dto.CompletedDate.Value.ToDateTime(TimeOnly.MinValue);
        else if (dto.Status == "completed")   ms.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return new DeliveryMilestoneDto(ms.Id, ms.Title, ms.DueDate, ms.CompletedAt, ms.Notes, ms.CreatedAt);
    }

    public async Task DeleteMilestoneAsync(Guid milestoneId, Guid orderId, Guid orgId, CancellationToken ct = default)
    {
        var ms = await db.DeliveryMilestones
            .FirstOrDefaultAsync(m => m.Id == milestoneId && m.OrderId == orderId && m.OrgId == orgId, ct)
            ?? throw new KeyNotFoundException("Milestone not found.");
        db.DeliveryMilestones.Remove(ms);
        await db.SaveChangesAsync(ct);
    }

    private static OrderDetailDto MapToDetail(Order o) => new(
        o.Id, o.OrgId, o.BidId, o.Bid?.Title,
        o.TenderId, o.Tender?.GemTenderId,
        o.GemOrderId, o.OrderNumber, o.BuyerOrg,
        o.OrderDate, o.DeliveryDate, o.TotalValue, o.Status,
        o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemDto(i.Id, i.Description, i.Quantity, i.UnitPrice, i.TotalPrice, i.HsnCode, i.CreatedAt)).ToList(),
        o.Milestones.Select(m => new DeliveryMilestoneDto(m.Id, m.Title, m.DueDate, m.CompletedAt, m.Notes, m.CreatedAt)).ToList());
}
