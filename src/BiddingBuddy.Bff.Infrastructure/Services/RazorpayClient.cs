using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BiddingBuddy.Bff.Core.Exceptions;
using BiddingBuddy.Bff.Core.Interfaces;
using BiddingBuddy.Bff.Core.Options;
using Microsoft.Extensions.Options;

namespace BiddingBuddy.Bff.Infrastructure.Services;

/// <summary>
/// Typed HttpClient for the Razorpay REST API (Basic auth KeyId:KeySecret). Only order
/// creation is needed in v1; activation trusts the verified checkout/webhook signatures,
/// not a status poll.
/// </summary>
public class RazorpayClient(HttpClient http, IOptions<RazorpayOptions> options) : IRazorpayClient
{
    private readonly RazorpayOptions _options = options.Value;

    public async Task<RazorpayOrder> CreateOrderAsync(
        long amountPaise, string currency, string receipt,
        IReadOnlyDictionary<string, string> notes, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("CHECKOUT_UNAVAILABLE");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.razorpay.com/v1/orders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.KeyId}:{_options.KeySecret}")));
        request.Content = JsonContent.Create(new
        {
            amount   = amountPaise,
            currency,
            receipt,
            notes,
        });

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new UpstreamServiceException(
                "Razorpay", $"Order creation failed: {body}", (int)response.StatusCode);

        var order = JsonSerializer.Deserialize<OrderResponse>(body)
            ?? throw new UpstreamServiceException("Razorpay", "Order creation returned an empty body.");

        return new RazorpayOrder(order.Id, order.Amount, order.Currency, order.Status);
    }

    private sealed record OrderResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("amount")] long Amount,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("status")] string Status);
}
