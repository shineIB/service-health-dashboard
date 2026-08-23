using Xunit;

namespace InventoryService.Api.Tests;

[CollectionDefinition(Name)]
public class InventoryApiCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Inventory API collection";
}
