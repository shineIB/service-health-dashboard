using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using InventoryService.Domain;

namespace InventoryService.Api;

public sealed class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
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
