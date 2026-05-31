namespace BiddingBuddy.Bff.Core.DTOs.Orders;

public record OrderListItemDto(
    Guid Id,
    string? GemOrderId,
    string? OrderNumber,
    string? BuyerOrg,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    decimal? TotalValue,
    string Status,
    Guid? BidId,
    string? BidTitle,
    DateTime UpdatedAt
);

public record OrderDetailDto(
    Guid Id,
    Guid OrgId,
    Guid? BidId,
    string? BidTitle,
    Guid? TenderId,
    string? GemTenderId,
    string? GemOrderId,
    string? OrderNumber,
    string? BuyerOrg,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    decimal? TotalValue,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<DeliveryMilestoneDto> Milestones
);

public record OrderItemDto(
    Guid Id,
    string Description,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    string? HsnCode,
    DateTime CreatedAt
);

public record DeliveryMilestoneDto(
    Guid Id,
    string Title,
    DateOnly? DueDate,
    DateTime? CompletedAt,
    string? Notes,
    DateTime CreatedAt
);

public record CreateOrderDto(
    Guid? BidId,
    Guid? TenderId,
    string? GemOrderId,
    string? OrderNumber,
    string? BuyerOrg,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    decimal? TotalValue
);

public record UpdateOrderDto(
    string? GemOrderId,
    string? OrderNumber,
    string? BuyerOrg,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    decimal? TotalValue,
    string? Status
);

public record CreateOrderItemDto(
    string Description,
    int Quantity,
    decimal UnitPrice,
    string? HsnCode
);

public record UpdateMilestoneDto(
    string? Title,
    DateOnly? DueDate,
    DateOnly? CompletedDate,
    string? Status,
    string? Notes
);

public record CreateMilestoneDto(string Title, DateOnly? DueDate, string? Notes);
