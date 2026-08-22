namespace InventoryService.Domain;

// Not sealed: InsufficientStockException derives from it so a generic exception
// handler can still catch every domain rule violation, while the API layer maps
// the insufficient-stock case to a different status code (409, not 400) — it's a
// valid business outcome, not an invalid request.
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
