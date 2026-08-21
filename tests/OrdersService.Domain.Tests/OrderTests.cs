using FluentAssertions;
using OrdersService.Domain;
using Xunit;

namespace OrdersService.Domain.Tests;

public class OrderTests
{
    private static OrderLine ValidLine(int quantity = 1, decimal unitPrice = 10m) =>
        new(Guid.NewGuid(), quantity, unitPrice);

    [Fact]
    public void Create_WithValidData_ReturnsPendingOrderWithComputedTotal()
    {
        var customerId = Guid.NewGuid();
        var lines = new[] { ValidLine(quantity: 2, unitPrice: 10m), ValidLine(quantity: 1, unitPrice: 5m) };

        var order = Order.Create(customerId, lines);

        order.CustomerId.Should().Be(customerId);
        order.Status.Should().Be(OrderStatus.Pending);
        order.Lines.Should().HaveCount(2);
        order.Total.Should().Be(25m);
        order.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_WithEmptyCustomerId_Throws()
    {
        var act = () => Order.Create(Guid.Empty, [ValidLine()]);

        act.Should().Throw<DomainException>().WithMessage("*CustomerId*");
    }

    [Fact]
    public void Create_WithNoLines_Throws()
    {
        var act = () => Order.Create(Guid.NewGuid(), []);

        act.Should().Throw<DomainException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void Confirm_FromPending_TransitionsToConfirmed()
    {
        var order = Order.Create(Guid.NewGuid(), [ValidLine()]);

        order.Confirm();

        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Cancel_FromPending_TransitionsToCancelled()
    {
        var order = Order.Create(Guid.NewGuid(), [ValidLine()]);

        order.Cancel();

        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // Pending is the only status Confirm()/Cancel() may start from; every other
    // (starting status, action) pair below must throw. Enumerating the pairs
    // explicitly means a newly added OrderStatus forces a decision here instead
    // of silently falling through as "allowed".
    public static TheoryData<OrderStatus, string> InvalidTransitions()
    {
        var data = new TheoryData<OrderStatus, string>();

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            if (status != OrderStatus.Pending)
                data.Add(status, nameof(Order.Confirm));

            if (status != OrderStatus.Pending)
                data.Add(status, nameof(Order.Cancel));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidTransitions))]
    public void Confirm_or_Cancel_WhenNotPending_Throws(OrderStatus fromStatus, string action)
    {
        var order = OrderInStatus(fromStatus);

        Action act = action switch
        {
            nameof(Order.Confirm) => order.Confirm,
            nameof(Order.Cancel) => order.Cancel,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        act.Should().Throw<DomainException>();
        order.Status.Should().Be(fromStatus, "a failed transition must not change the order's status");
    }

    private static Order OrderInStatus(OrderStatus status)
    {
        var order = Order.Create(Guid.NewGuid(), [ValidLine()]);

        switch (status)
        {
            case OrderStatus.Pending:
                break;
            case OrderStatus.Confirmed:
                order.Confirm();
                break;
            case OrderStatus.Cancelled:
                order.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return order;
    }
}
