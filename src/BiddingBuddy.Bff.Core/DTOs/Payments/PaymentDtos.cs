namespace BiddingBuddy.Bff.Core.DTOs.Payments;

public record EmdPaymentDto(
    Guid Id,
    Guid? BidId,
    string? BidTitle,
    Guid? TenderId,
    string? GemTenderId,
    string? TenderTitle,
    decimal Amount,
    DateOnly PaymentDate,
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string Status,
    DateOnly? RefundDate,
    decimal? RefundAmount,
    string? RefundRef,
    string? Notes,
    DateTime UpdatedAt
);

public record CreateEmdDto(
    Guid? BidId,
    Guid? TenderId,
    string? GemTenderId,
    string? TenderTitle,
    decimal Amount,
    DateOnly PaymentDate,
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string? Notes
);

public record UpdateEmdDto(
    string? PaymentMode,
    string? TransactionRef,
    string? BankName,
    string? Status,
    DateOnly? RefundDate,
    decimal? RefundAmount,
    string? RefundRef,
    string? Notes
);

public record InvoiceDto(
    Guid Id,
    Guid? OrderId,
    string? OrderNumber,
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal Amount,
    decimal? GstAmount,
    decimal? TotalAmount,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    DateOnly? PaidDate,
    decimal? PaidAmount,
    string Status,
    string? PaymentRef,
    string? Notes,
    DateTime UpdatedAt
);

public record CreateInvoiceDto(
    Guid? OrderId,
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal Amount,
    decimal? GstAmount,
    DateOnly InvoiceDate,
    DateOnly? DueDate,
    string? Notes
);

public record UpdateInvoiceDto(
    string? InvoiceNumber,
    string? BuyerOrg,
    decimal? Amount,
    decimal? GstAmount,
    decimal? TotalAmount,
    DateOnly? InvoiceDate,
    DateOnly? DueDate,
    DateOnly? PaidDate,
    decimal? PaidAmount,
    string? Status,
    string? PaymentRef,
    string? Notes
);
