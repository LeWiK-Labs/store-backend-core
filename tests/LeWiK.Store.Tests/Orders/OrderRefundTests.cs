using LeWiK.Store.App.Orders.Domain;

namespace LeWiK.Tienda.Tests.Orders;

// Refunds are a second axis on the order, deliberately not a reversal of the first one.
// These tests pin that separation down: what a refund moves, and what it must leave alone.
public class OrderRefundTests
{
    private static OrderLineDraft Line(int qty, decimal price, bool preorder = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"SKU-{price}", "Item", price, "CLP", qty, preorder);

    private static Order PaidOrder(decimal total = 20000)
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, total / 2)], 0).Value;
        order.ApplyPayment(total);
        return order;
    }

    [Fact]
    public void Partial_refund_tracks_amount_without_changing_payment_status()
    {
        var order = PaidOrder(20000);

        var result = order.ApplyRefund(5000);

        Assert.True(result.IsSuccess);
        Assert.Equal(5000, order.RefundedAmount);
        Assert.Equal(20000, order.PaidAmount);                  // gross paid unchanged
        Assert.Equal(15000, order.NetPaidAmount);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);  // records what was charged
    }

    [Fact]
    public void Refunds_accumulate_up_to_the_paid_amount()
    {
        var order = PaidOrder(20000);
        order.ApplyRefund(8000);
        order.ApplyRefund(12000);

        Assert.Equal(20000, order.RefundedAmount);
        Assert.Equal(0, order.NetPaidAmount);
    }

    [Fact]
    public void Refund_cannot_exceed_what_was_paid()
    {
        var order = PaidOrder(20000);
        order.ApplyRefund(15000);

        var result = order.ApplyRefund(10000); // only 5000 left

        Assert.True(result.IsFailure);
        Assert.Equal("order.refund_exceeds_paid", result.Error.Code);
        Assert.Equal(15000, order.RefundedAmount); // unchanged
    }

    [Fact]
    public void Refund_on_unpaid_order_fails()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(1, 10000)], 0).Value;

        var result = order.ApplyRefund(1000);

        Assert.True(result.IsFailure);
        Assert.Equal("order.refund_exceeds_paid", result.Error.Code);
    }

    [Fact]
    public void Fully_refunded_order_still_refuses_a_new_payment()
    {
        // The whole reason PaymentStatus does not walk backwards: if refunding reopened the
        // order for payment, a refunded buyer could be charged again on the same balance.
        var order = PaidOrder(20000);
        order.ApplyRefund(20000);

        var result = order.ApplyPayment(20000);

        Assert.True(result.IsFailure);
        Assert.Equal("order.already_paid", result.Error.Code);
        Assert.Equal(0, order.BalanceAmount);
    }

    [Fact]
    public void Refunding_a_deposit_does_not_reopen_fulfillment()
    {
        // Deposited order: returning the deposit is a money movement, not a fulfillment one.
        // Releasing stock is what Cancel is for, and the two are separate decisions.
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000)], 8000).Value;
        order.ApplyPayment(8000);
        var stageBefore = order.FulfillmentStatus;

        order.ApplyRefund(8000);

        Assert.Equal(PaymentStatus.Deposited, order.PaymentStatus);
        Assert.Equal(stageBefore, order.FulfillmentStatus);
        Assert.Equal(0, order.NetPaidAmount);
        Assert.Equal(12000, order.BalanceAmount);   // balance still derives from gross paid
    }

    [Fact]
    public void Cancelled_order_can_still_be_refunded()
    {
        // The realistic sequence: cancel releases the stock, refund returns the money.
        var order = PaidOrder(20000);
        order.Cancel();

        var result = order.ApplyRefund(20000);

        Assert.True(result.IsSuccess);
        Assert.Equal(20000, order.RefundedAmount);
        Assert.Equal(FulfillmentStatus.Cancelled, order.FulfillmentStatus);
    }

    [Fact]
    public void Refund_raises_an_event_carrying_the_amount()
    {
        var order = PaidOrder(20000);
        order.ClearDomainEvents();

        order.ApplyRefund(7500);

        var raised = Assert.Single(order.DomainEvents.OfType<OrderRefunded>());
        Assert.Equal(order.Id, raised.OrderId);
        Assert.Equal(7500, raised.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Refund_must_be_positive(decimal amount)
    {
        var order = PaidOrder(20000);

        Assert.Throws<ArgumentOutOfRangeException>(() => order.ApplyRefund(amount));
    }
}
