using FluentAssertions;
using InventoryService.Domain;
using Xunit;

namespace InventoryService.Domain.Tests;

public class InventoryItemTests
{
    [Fact]
    public void Create_WithValidData_ReturnsItemWithFullyAvailableStock()
    {
        var productId = Guid.NewGuid();

        var item = InventoryItem.Create(productId, 100);

        item.ProductId.Should().Be(productId);
        item.AvailableQuantity.Should().Be(100);
        item.ReservedQuantity.Should().Be(0);
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

        item.Reserve(4);

        item.AvailableQuantity.Should().Be(6);
        item.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public void Reserve_WithInsufficientStock_Throws()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 3);

        var act = () => item.Reserve(4);

        act.Should().Throw<DomainException>().WithMessage("*Insufficient stock*");
        item.AvailableQuantity.Should().Be(3, "a failed reservation must not change stock");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Reserve_WithNonPositiveQuantity_Throws(int quantity)
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);

        var act = () => item.Reserve(quantity);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Release_WithSufficientReservedStock_MovesQuantityBackToAvailable()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(4);

        item.Release(4);

        item.AvailableQuantity.Should().Be(10);
        item.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Release_MoreThanReserved_Throws()
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(2);

        var act = () => item.Release(3);

        act.Should().Throw<DomainException>().WithMessage("*only 2 reserved*");
        item.ReservedQuantity.Should().Be(2, "a failed release must not change stock");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Release_WithNonPositiveQuantity_Throws(int quantity)
    {
        var item = InventoryItem.Create(Guid.NewGuid(), 10);
        item.Reserve(5);

        var act = () => item.Release(quantity);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }
}
