using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Orders;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IOrderService
{
    Task<PagedResult<OrderListItemDto>> ListAsync(Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<OrderDetailDto> GetAsync(Guid orderId, Guid orgId, CancellationToken ct = default);
    Task<OrderDetailDto> CreateAsync(Guid orgId, CreateOrderDto dto, CancellationToken ct = default);
    Task<OrderDetailDto> UpdateAsync(Guid orderId, Guid orgId, UpdateOrderDto dto, CancellationToken ct = default);

    Task<OrderItemDto> AddItemAsync(Guid orderId, Guid orgId, CreateOrderItemDto dto, CancellationToken ct = default);
    Task DeleteItemAsync(Guid itemId, Guid orderId, Guid orgId, CancellationToken ct = default);

    Task<DeliveryMilestoneDto> AddMilestoneAsync(Guid orderId, Guid orgId, CreateMilestoneDto dto, CancellationToken ct = default);
    Task<DeliveryMilestoneDto> UpdateMilestoneAsync(Guid milestoneId, Guid orderId, Guid orgId, UpdateMilestoneDto dto, CancellationToken ct = default);
    Task DeleteMilestoneAsync(Guid milestoneId, Guid orderId, Guid orgId, CancellationToken ct = default);
}
