using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using InventoryService.Api.Telemetry;
using InventoryService.Domain;

namespace InventoryService.Api;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Insufficient stock is a valid business outcome, not an invalid request —
        // it gets 409 so callers (and their retry policies) can tell it apart from
        // a genuinely malformed request (400) or a transient failure (5xx/408).
        if (exception is InsufficientStockException insufficientStockException)
        {
            // Counted here, not in the reserve endpoint itself: item.Reserve throws before
            // returning, so this exception handler is the only place every insufficient-stock
            // rejection actually passes through.
            InventoryTelemetry.ReservationsFailed.Add(1, new KeyValuePair<string, object?>("reason", "insufficient_stock"));

            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;

            await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Insufficient stock.",
                Detail = insufficientStockException.Message
            }, cancellationToken);

            return true;
        }

        if (exception is not DomainException domainException)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid inventory operation.",
            Detail = domainException.Message
        }, cancellationToken);

        return true;
    }
}
