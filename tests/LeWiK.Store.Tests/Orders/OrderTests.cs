using LeWiK.Store.App.Orders.Domain;

namespace LeWiK.Tienda.Tests.Orders;

public class OrderTests
{
    private static OrderLineDraft Line(int qty, decimal price, bool preorder = false) =>
        new(Guid.NewGuid(), $"SKU-{price}", "Item", price, "CLP", qty, preorder);

    private static Order PlaceSimple(decimal price = 10000, int qty = 2, decimal deposit = 0) =>
        Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(qty, price)], deposit).Value;

    [Fact]
    public void Place_computes_total_and_starts_pending()
    {
        var order = PlaceSimple(price: 10000, qty: 2);
        Assert.Equal(20000, order.TotalAmount);
        Assert.Equal(FulfillmentStatus.PendingPayment, order.FulfillmentStatus);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
    }

    [Fact]
    public void Place_rejects_empty_order()
    {
        var result = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [], 0);
        Assert.True(result.IsFailure);
        Assert.Equal("order.empty", result.Error.Code);
    }

    [Fact]
    public void Full_payment_no_preorder_moves_to_paid()
    {
        var order = PlaceSimple(price: 10000, qty: 2); // total 20000
        var result = order.ApplyPayment(20000);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.Paid, order.FulfillmentStatus);
        Assert.Equal(0, order.BalanceAmount);
    }

    [Fact]
    public void Deposit_moves_payment_to_deposited_and_awaits_release()
    {
        // total 20000, deposit required 6000
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        var result = order.ApplyPayment(6000);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Deposited, order.PaymentStatus);
        Assert.Equal(FulfillmentStatus.AwaitingRelease, order.FulfillmentStatus);
        Assert.Equal(14000, order.BalanceAmount);
    }

    [Fact]
    public void Balance_payment_completes_and_release_is_allowed()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        order.ApplyPayment(6000);   // deposit
        order.ApplyPayment(14000);  // balance

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        var released = order.MarkReleased();
        Assert.True(released.IsSuccess);
        Assert.Equal(FulfillmentStatus.Paid, order.FulfillmentStatus);
    }

    [Fact]
    public void Release_blocked_while_balance_pending()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        order.ApplyPayment(6000); // only the deposit

        var released = order.MarkReleased();
        Assert.True(released.IsFailure);
        Assert.Equal("order.balance_pending", released.Error.Code);
    }

    [Fact]
    public void Payment_cannot_exceed_balance()
    {
        var order = PlaceSimple(price: 10000, qty: 1); // total 10000
        var result = order.ApplyPayment(15000);
        Assert.True(result.IsFailure);
        Assert.Equal("order.payment_exceeds_balance", result.Error.Code);
    }

    [Fact]
    public void Partial_then_full_line_fulfillment_derives_status()
    {
        var draftA = new OrderLineDraft(Guid.NewGuid(), "A", "A", 10000, "CLP", 2, false);
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [draftA], 0).Value;
        order.ApplyPayment(20000);
        order.StartPreparing();

        var lineId = order.Lines.First().Id;

        var partial = order.FulfillLine(lineId, 1); // 1 of 2
        Assert.True(partial.IsSuccess);
        Assert.Equal(FulfillmentStatus.PartiallyDelivered, order.FulfillmentStatus);

        var rest = order.FulfillLine(lineId, 1);    // the other 1
        Assert.True(rest.IsSuccess);
        Assert.Equal(FulfillmentStatus.Delivered, order.FulfillmentStatus);
    }

    [Fact]
    public void Cannot_fulfill_more_than_ordered()
    {
        var order = PlaceSimple(price: 10000, qty: 2);
        order.ApplyPayment(20000);
        order.StartPreparing();
        var lineId = order.Lines.First().Id;

        var result = order.FulfillLine(lineId, 5);
        Assert.True(result.IsFailure);
        Assert.Equal("order.fulfill_exceeds_pending", result.Error.Code);
    }

    [Fact]
    public void Preparing_blocked_before_full_payment()
    {
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        order.ApplyPayment(6000); // deposited, not paid

        var result = order.StartPreparing();
        Assert.True(result.IsFailure); // not in Paid status
    }
}