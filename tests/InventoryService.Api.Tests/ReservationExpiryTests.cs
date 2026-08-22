using System.Net.Http.Json;
using FluentAssertions;
using InventoryService.Api.Contracts;
using Xunit;

namespace InventoryService.Api.Tests;

public class ReservationExpiryTests : IClassFixture<ReservationExpiryFactory>
{
    private readonly ReservationExpiryFactory _factory;

    public ReservationExpiryTests(ReservationExpiryFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Reservation_PastTtl_IsAutomaticallyReleasedByTheBackgroundSweep()
    {
        var client = _factory.CreateClient();
        var productId = Guid.NewGuid();
        await client.PostAsJsonAsync("/inventory", new { productId, initialQuantity = 10 });
        await client.PostAsJsonAsync($"/inventory/{productId}/reserve", new { orderId = Guid.NewGuid(), quantity = 4 });

        // No /release call: the reservation's 1s TTL and the 1s sweep interval
        // (configured in ReservationExpiryFactory) should reclaim the stock on their own.
        await Task.Delay(TimeSpan.FromSeconds(3));

        var response = await client.GetAsync($"/inventory/{productId}");
        var body = await response.Content.ReadFromJsonAsync<InventoryItemResponse>();

        body!.AvailableQuantity.Should().Be(10, "the background sweep should have expired the stale reservation");
        body.ReservedQuantity.Should().Be(0);
    }
}
