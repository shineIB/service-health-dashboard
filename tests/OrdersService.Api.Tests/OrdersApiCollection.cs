using Xunit;

namespace OrdersService.Api.Tests;

[CollectionDefinition(Name)]
public class OrdersApiCollection : ICollectionFixture<PostgresContainerFixture>, ICollectionFixture<RabbitMqContainerFixture>
{
    public const string Name = "Orders API collection";
}
