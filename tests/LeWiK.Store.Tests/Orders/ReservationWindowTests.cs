using LeWiK.Store.App.Orders.Domain;

namespace LeWiK.Tienda.Tests.Orders;

// The soft-lock window: how long an unpaid order holds its stock before the sweeper gives it back.
public class ReservationWindowTests
{
    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0, DateTimeKind.Utc);

    private static OrderLineDraft Line(int qty, decimal price, bool preorder = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"SKU-{price}", "Item", price, "CLP", qty, preorder);

    private static Order Place(decimal price = 10000, int qty = 2, decimal deposit = 0) =>
        Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(qty, price)], deposit).Value;

    [Fact]
    public void Place_leaves_the_order_off_the_clock()
    {
        // The window is opened by the checkout handler from config, not by the aggregate: with
        // reservations disabled an order must never acquire a deadline.
        Assert.Null(Place().ReservationExpiresAt);
    }

    [Fact]
    public void Setting_the_window_puts_the_order_on_the_clock()
    {
        var order = Place();
        order.SetReservationWindow(Now.AddMinutes(30));
        Assert.Equal(Now.AddMinutes(30), order.ReservationExpiresAt);
    }

    [Fact]
    public void Extending_pushes_the_deadline_out()
    {
        var order = Place();
        order.SetReservationWindow(Now.AddMinutes(5));

        order.ExtendReservation(Now.AddMinutes(20));

        Assert.Equal(Now.AddMinutes(20), order.ReservationExpiresAt);
    }

    [Fact]
    public void Extending_never_shortens_the_window()
    {
        // A gateway flow that starts with 25 minutes left must not be cut to the 20 of the grace
        // period: the buyer would lose time by starting to pay.
        var order = Place();
        order.SetReservationWindow(Now.AddMinutes(25));

        order.ExtendReservation(Now.AddMinutes(20));

        Assert.Equal(Now.AddMinutes(25), order.ReservationExpiresAt);
    }

    [Fact]
    public void Extending_an_order_with_no_window_leaves_it_off_the_clock()
    {
        var order = Place();
        order.ExtendReservation(Now.AddMinutes(20));
        Assert.Null(order.ReservationExpiresAt);
    }

    [Fact]
    public void A_partial_payment_stops_the_clock_for_good()
    {
        // ⭐ The money rule: one peso received means a human decides from here on. Cancelling this
        // order automatically would create a refund with nobody in the loop.
        var order = Place(price: 10000, qty: 2);         // total 20000
        order.SetReservationWindow(Now.AddMinutes(30));

        order.ApplyPayment(5000);

        Assert.Equal(PaymentStatus.Deposited, order.PaymentStatus);
        Assert.Null(order.ReservationExpiresAt);
    }

    [Fact]
    public void A_full_payment_stops_the_clock()
    {
        var order = Place(price: 10000, qty: 2);
        order.SetReservationWindow(Now.AddMinutes(30));

        order.ApplyPayment(20000);

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Null(order.ReservationExpiresAt);
    }

    [Fact]
    public void Cancelling_clears_the_window()
    {
        var order = Place();
        order.SetReservationWindow(Now.AddMinutes(30));

        order.Cancel();

        Assert.Null(order.ReservationExpiresAt);
    }

    [Theory]
    [InlineData(-1, true)]      // deadline already past
    [InlineData(1, false)]      // still has time
    public void Expiry_depends_on_the_deadline(int minutesFromNow, bool expected)
    {
        var order = Place();
        order.SetReservationWindow(Now.AddMinutes(minutesFromNow));
        Assert.Equal(expected, order.IsReservationExpired(Now));
    }

    [Fact]
    public void An_order_that_was_never_on_the_clock_never_expires()
    {
        Assert.False(Place().IsReservationExpired(Now));
    }

    [Fact]
    public void A_paid_order_does_not_expire_even_with_a_lapsed_window()
    {
        // The payment already cleared the window, so this re-sets it to prove the guard reads the
        // payment status itself. It has to: the worker's whole job is to not cancel paid orders,
        // and that must not depend on one caller having remembered to clear a field.
        var order = Place(price: 10000, qty: 2);
        order.ApplyPayment(5000);
        order.SetReservationWindow(Now.AddMinutes(-10));

        Assert.False(order.IsReservationExpired(Now));
    }

    [Fact]
    public void An_already_cancelled_order_does_not_expire_again()
    {
        var order = Place();
        order.Cancel();
        order.SetReservationWindow(Now.AddMinutes(-10));

        Assert.False(order.IsReservationExpired(Now));
    }

    [Fact]
    public void An_awaiting_release_preorder_does_not_expire()
    {
        // A deposited drop is waiting for stock, not for payment. It holds its capacity on purpose
        // and for weeks; the sweeper only ever touches PendingPayment.
        var order = Order.Place(Guid.NewGuid(), Guid.NewGuid(), "CLP", [Line(2, 10000, preorder: true)], 6000).Value;
        order.ApplyPayment(6000);
        order.SetReservationWindow(Now.AddMinutes(-10));

        Assert.Equal(FulfillmentStatus.AwaitingRelease, order.FulfillmentStatus);
        Assert.False(order.IsReservationExpired(Now));
    }
}
