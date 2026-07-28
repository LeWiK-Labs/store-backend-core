using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

// Once there is nothing left to pay, the link stops being useful — retire it instead of
// leaving a working payment URL sitting in someone's chat history until it expires.
//
// This is the first handler that reacts to a domain event by CHANGING state, so it doubles as
// the liveness check on dispatch: if a paid order's link still resolves, events are not firing.
public sealed class RevokePaymentLinkOnPaid(StoreDbContext db) : INotificationHandler<OrderPaid>
{
    public async Task Handle(OrderPaid notification, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == notification.OrderId, ct);
        if (order?.PaymentLinkHash is null) return;

        order.RevokePaymentLink();
        // Events are dispatched after the unit of work already committed, so this is its own
        // write rather than part of the payment's transaction.
        await db.SaveChangesAsync(ct);
    }
}
