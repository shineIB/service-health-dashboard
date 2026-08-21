using FluentAssertions;
using OrdersService.Domain;
using Xunit;

namespace OrdersService.Domain.Tests;

public class OrderLineTests
{
    [Fact]
    public void Constructor_WithValidData_SetsPropertiesAndComputesLineTotal()
    {
        var productId = Guid.NewGuid();

        var line = new OrderLine(productId, quantity: 3, unitPrice: 10.50m);

        line.ProductId.Should().Be(productId);
        line.Quantity.Should().Be(3);
        line.UnitPrice.Should().Be(10.50m);
        line.LineTotal.Should().Be(31.50m);
    }

    [Fact]
    public void Constructor_WithEmptyProductId_Throws()
    {
        var act = () => new OrderLine(Guid.Empty, quantity: 1, unitPrice: 10m);

        act.Should().Throw<DomainException>().WithMessage("*ProductId*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveQuantity_Throws(int quantity)
    {
        var act = () => new OrderLine(Guid.NewGuid(), quantity, unitPrice: 10m);

        act.Should().Throw<DomainException>().WithMessage("*Quantity*");
    }

    [Fact]
    public void Constructor_WithNegativeUnitPrice_Throws()
    {
        var act = () => new OrderLine(Guid.NewGuid(), quantity: 1, unitPrice: -0.01m);

        act.Should().Throw<DomainException>().WithMessage("*UnitPrice*");
    }
}
