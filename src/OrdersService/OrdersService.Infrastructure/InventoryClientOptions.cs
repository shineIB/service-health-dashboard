namespace OrdersService.Infrastructure;

public sealed class InventoryClientOptions
{
    public const string SectionName = "InventoryClient";

    public required string BaseUrl { get; init; }
}
