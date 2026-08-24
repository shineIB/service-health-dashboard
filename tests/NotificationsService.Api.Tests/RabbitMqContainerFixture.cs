using Testcontainers.RabbitMq;
using Xunit;

namespace NotificationsService.Api.Tests;

// Real RabbitMQ, not a fake — same reasoning as OrdersService/InventoryService's
// PostgresContainerFixture (see CLAUDE.md: the project moved away from mocking infra
// dependencies in tests once Testcontainers made a real instance cheap). Shared per
// collection so every WebApplicationFactory in the collection reuses the same container.
public sealed class RabbitMqContainerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    public string HostName => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5672);

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
