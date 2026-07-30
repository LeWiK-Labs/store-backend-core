using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

// Used to fire on OrderPaid, which stopped being right once the token became the guest's
// ACCESS to the order rather than just permission to pay it: revoking at payment would lock a
// buyer out of the order they had just paid for, with no account to fall back on.
//
// Delivery is the honest end of the buyer's need for it — the goods are in their hands, and
// there is nothing left to track or pay.
//
// It also remains the system's liveness check on domain-event dispatch: nobody calls this, the
// OrderDelivered event does. If a delivered order's link still resolves, events are not firing.
public sealed class RevokePaymentLinkOnDelivered(StoreDbContext db) : INotificationHandler<OrderDelivered>
{
    public async Task Handle(OrderDelivered notification, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == notification.OrderId, ct);
        if (order?.PaymentLinkHash is null) return;

        order.RevokePaymentLink();
        // Events are dispatched after the unit of work already committed, so this is its own write.
        await db.SaveChangesAsync(ct);
    }
}
