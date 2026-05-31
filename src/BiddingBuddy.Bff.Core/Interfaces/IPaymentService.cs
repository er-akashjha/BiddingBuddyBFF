using BiddingBuddy.Bff.Core.DTOs.Common;
using BiddingBuddy.Bff.Core.DTOs.Payments;

namespace BiddingBuddy.Bff.Core.Interfaces;

public interface IPaymentService
{
    Task<PagedResult<EmdPaymentDto>> ListEmdAsync(Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<EmdPaymentDto> GetEmdAsync(Guid emdId, Guid orgId, CancellationToken ct = default);
    Task<EmdPaymentDto> CreateEmdAsync(Guid orgId, CreateEmdDto dto, CancellationToken ct = default);
    Task<EmdPaymentDto> UpdateEmdAsync(Guid emdId, Guid orgId, UpdateEmdDto dto, CancellationToken ct = default);

    Task<PagedResult<InvoiceDto>> ListInvoicesAsync(Guid orgId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId, Guid orgId, CancellationToken ct = default);
    Task<InvoiceDto> CreateInvoiceAsync(Guid orgId, CreateInvoiceDto dto, CancellationToken ct = default);
    Task<InvoiceDto> UpdateInvoiceAsync(Guid invoiceId, Guid orgId, UpdateInvoiceDto dto, CancellationToken ct = default);
}
