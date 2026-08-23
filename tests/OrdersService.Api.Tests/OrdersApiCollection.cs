using Xunit;

namespace OrdersService.Api.Tests;

[CollectionDefinition(Name)]
public class OrdersApiCollection : ICollectionFixture<PostgresContainerFixture>
{
    public const string Name = "Orders API collection";
}
