using FluentAssertions;
using InventoryService.Domain;
using Xunit;

namespace InventoryService.Domain.Tests;

public class InventoryItemTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    [Fact]
    public void Create_WithValidData_ReturnsItemWithFullyAvailableStock()
    {
        var productId = Guid.NewGuid();

        var item = InventoryItem.Create(productId, 100);

        item.ProductId.Should().Be(productId);
        item.AvailableQuantity.Should().Be(100);
        item.ReservedQuantity.Should().Be(0);
        item.Reservations.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithEmptyProductId_Throws()
    {
        var act = () => InventoryItem.Create(Guid.Empty, 10);

        act.Should().Throw<DomainException>().WithMessage("*ProductId*");
    }

    [Fact]
    public void Create_WithNegativeQuantity_Throws()
    {
        var act = () => InventoryItem.Create(Guid.NewGuid(), -1);

        act.Should().Throw<DomainException>().WithMessage("*negative*");
    }

    [Fact]
    public void Reserve_WithSufficientStock_MovesQuantityFromAvailableToReserved()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        item.AvailableQuantity.Should().Be(6);
        item.ReservedQuantity.Should().Be(4);
        item.Reservations.Should().ContainSingle();
    }

    [Fact]
    public void Reserve_WithInsufficientStock_ThrowsInsufficientStockException()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 3);

        var act = () => item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        act.Should().Throw<InsufficientStockException>().WithMessage("*Insufficient stock*");
        item.AvailableQuantity.Should().Be(3, "a failed reservation must not change stock");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_WithNonPositiveQuantity_Throws(int quantity)
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        var act = () => item.Reserve(Guid.NewGuid(), quantity, Ttl, NowUtc);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Reserve_CalledTwiceWithSameOrderId_IsIdempotent()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        var orderId = Guid.NewGuid();

        item.Reserve(orderId, 4, Ttl, NowUtc);
        item.Reserve(orderId, 4, Ttl, NowUtc);

        item.AvailableQuantity.Should().Be(6, "a retried reservation for the same order must not reserve twice");
        item.ReservedQuantity.Should().Be(4);
        item.Reservations.Should().ContainSingle();
    }

    [Fact]
    public void Reserve_RetriedAfterStockWasFullyConsumedByOthers_StillSucceedsForTheSameOrder()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 4);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 4, Ttl, NowUtc);

        // Simulates a client retry (e.g. after a dropped response) for the same order,
        // even though there is now no available stock left for a *new* reservation.
        var act = () => item.Reserve(orderId, 4, Ttl, NowUtc);

        act.Should().NotThrow();
        item.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public void Release_WithActiveReservation_MovesQuantityBackToAvailable()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        var orderId = Guid.NewGuid();
        item.Reserve(orderId, 4, Ttl, NowUtc);

        item.Release(orderId);

        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
        item.Reservations.Should().BeEmpty();
    }

    [Fact]
    public void Release_ForUnknownOrderId_IsANoOp()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        var act = () => item.Release(Guid.NewGuid());

        act.Should().NotThrow();
        item.AvailableQuantity.Should().Be(6, "releasing an order that was never reserved must not touch stock");
    }

    [Fact]
    public void ExpireStaleReservations_PastTtl_ReleasesStockBackToAvailable()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        var expiredCount = item.ExpireStaleReservations(NowUtc + Ttl + TimeSpan.FromSeconds(1));

        expiredCount.Should().Be(1);
        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
        item.Reservations.Should().BeEmpty();
    }

    [Fact]
    public void ExpireStaleReservations_BeforeTtl_LeavesReservationIntact()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        var expiredCount = item.ExpireStaleReservations(NowUtc + Ttl - TimeSpan.FromSeconds(1));

        expiredCount.Should().Be(0);
        item.AvailableQuantity.Should().Be(6);
        item.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public void Reserve_AfterAnotherOrdersReservationExpired_CanReuseTheReclaimedStock()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 4);
        item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc);

        // Reserve() sweeps expired reservations for this item before evaluating
        // availability, so a new order can reuse stock an expired one leaked back.
        var act = () => item.Reserve(Guid.NewGuid(), 4, Ttl, NowUtc + Ttl + TimeSpan.FromSeconds(1));

        act.Should().NotThrow();
        item.ReservedQuantity.Should().Be(4);
    }
}
