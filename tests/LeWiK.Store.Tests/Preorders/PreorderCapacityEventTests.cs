using LeWiK.Store.App.Preorders.Domain;

namespace LeWiK.Tienda.Tests.Preorders;

// Preorder became an AggregateRoot in 4.7 so the live counter has something to listen to during
// a drop, which is the one moment it matters. These tests pin what the event says, because a
// counter that lies is worse than no counter: it tells a buyer to keep clicking on a drop that
// is already gone.
public class PreorderCapacityEventTests
{
    private static Preorder NewPreorder(int capacity = 100) =>
        new(Guid.NewGuid(), Guid.NewGuid(), capacity, DateTime.UtcNow.AddDays(30), DepositType.Percentage, 30);

    private static PreorderCapacityChanged? Last(Preorder p) =>
        p.DomainEvents.OfType<PreorderCapacityChanged>().LastOrDefault();

    private static int Count(Preorder p) => p.DomainEvents.OfType<PreorderCapacityChanged>().Count();

    [Fact]
    public void Creating_a_drop_announces_it()
    {
        // A variant that was selling from stock now sells from capacity: anyone watching it would
        // otherwise keep showing the stock number with no event ever coming to correct it.
        var p = NewPreorder(capacity: 50);
        var e = Last(p);

        Assert.NotNull(e);
        Assert.Equal(p.TenantId, e.TenantId);
        Assert.Equal(p.ProductVariantId, e.ProductVariantId);
        Assert.Equal(50, e.AvailableCapacity);
        Assert.True(e.IsSellable);
    }

    [Fact]
    public void ReserveCapacity_reports_what_is_left()
    {
        var p = NewPreorder(capacity: 10);
        p.ClearDomainEvents();

        p.ReserveCapacity(4);

        Assert.Equal(6, Last(p)!.AvailableCapacity);
        Assert.Equal(4, Last(p)!.SoldCount);
    }

    [Fact]
    public void ReleaseCapacity_reports_the_capacity_coming_back()
    {
        // The path an expired reservation takes (4.6): the counter has to go UP on its own.
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(3);
        p.ClearDomainEvents();

        p.ReleaseCapacity(3);

        Assert.Equal(10, Last(p)!.AvailableCapacity);
    }

    [Fact]
    public void A_refused_reservation_announces_nothing()
    {
        // Nothing moved, so nothing may be broadcast — a failed order must not repaint a counter.
        var p = NewPreorder(capacity: 3);
        p.ClearDomainEvents();

        var result = p.ReserveCapacity(5);

        Assert.True(result.IsFailure);
        Assert.Equal(0, Count(p));
    }

    [Fact]
    public void A_refused_reconfigure_announces_nothing()
    {
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(6);
        p.ClearDomainEvents();

        var result = p.Reconfigure(4, DateTime.UtcNow.AddDays(10), DepositType.None, 0);

        Assert.True(result.IsFailure);
        Assert.Equal(0, Count(p));
    }

    [Fact]
    public void Reconfigure_reports_the_new_capacity()
    {
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(2);
        p.ClearDomainEvents();

        p.Reconfigure(30, DateTime.UtcNow.AddDays(10), DepositType.Percentage, 20);

        Assert.Equal(28, Last(p)!.AvailableCapacity);
    }

    [Fact]
    public void Closing_a_drop_makes_it_unsellable_even_with_capacity_left()
    {
        // ⭐ The reason the event carries IsOpen and not just the numbers. A closed drop with 40
        // units on paper refuses every order (preorder.closed); broadcasting "40, go ahead" would
        // contradict both the storefront read — which only composes ACTIVE drops — and checkout.
        var p = NewPreorder(capacity: 40);
        p.ClearDomainEvents();

        p.Close();
        var e = Last(p)!;

        Assert.Equal(40, e.AvailableCapacity);
        Assert.False(e.IsOpen);
        Assert.False(e.IsSellable);
    }

    [Fact]
    public void An_open_drop_with_nothing_left_is_not_sellable()
    {
        var p = NewPreorder(capacity: 2);
        p.ReserveCapacity(2);
        var e = Last(p)!;

        Assert.Equal(0, e.AvailableCapacity);
        Assert.True(e.IsOpen);
        Assert.False(e.IsSellable);
    }

    [Fact]
    public void Events_are_cleared_once_dispatched()
    {
        // UnitOfWorkBehavior clears after publishing; a second command on the same tracked
        // aggregate must not replay the first one's frames.
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(1);
        Assert.Equal(2, Count(p));

        p.ClearDomainEvents();

        Assert.Empty(p.DomainEvents);
    }
}
