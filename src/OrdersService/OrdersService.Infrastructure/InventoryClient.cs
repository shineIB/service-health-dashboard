using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using OrdersService.Domain;

namespace OrdersService.Infrastructure;

// Minimal local shape for inventory-service's ProblemDetails response body: this project
// has no ASP.NET Core framework reference (it's a plain class library), so it can't use
// Microsoft.AspNetCore.Mvc.ProblemDetails directly.
internal sealed record ProblemDetailsPayload(string? Detail);

public sealed class InventoryClient : IInventoryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InventoryClient> _logger;

    public InventoryClient(HttpClient httpClient, ILogger<InventoryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ReserveStockResult> ReserveStockAsync(
        Guid orderId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/inventory/{productId}/reserve",
                new { orderId, quantity },
                cancellationToken);

            if (response.IsSuccessStatusCode)
                return ReserveStockResult.Reserved();

            // 409/404 are valid business outcomes from inventory-service, not transient
            // failures — the resilience handler's retry policy already excludes them by
            // default (it only retries 5xx/408/429 and network/timeout exceptions), so
            // reaching this branch means the request completed and was answered.
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsPayload>(cancellationToken: cancellationToken);
                return ReserveStockResult.InsufficientStock(problem?.Detail ?? "Insufficient stock.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return ReserveStockResult.InsufficientStock($"No inventory record for product {productId}.");

            _logger.LogWarning(
                "Unexpected response from inventory-service reserving product {ProductId}: {StatusCode}.",
                productId,
                response.StatusCode);
            return ReserveStockResult.Unavailable($"Unexpected response from inventory-service: {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException)
        {
            _logger.LogWarning(ex, "inventory-service unavailable while reserving stock for product {ProductId}.", productId);
            return ReserveStockResult.Unavailable("inventory-service is unavailable.");
        }
    }
}
