using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

// OnlyIfReservationExpired is how the expiry worker asks for a conditional cancel. It defaults to
// false, so the admin action is unchanged: a human cancelling an order means it, paid or not.
public sealed record CancelOrderCommand(Guid OrderId, bool OnlyIfReservationExpired = false)
    : ICommand<OrderPaymentResponse>;

public sealed class CancelOrderHandler(StoreDbContext db)
    : IRequestHandler<CancelOrderCommand, Result<OrderPaymentResponse>>
{
    public async Task<Result<OrderPaymentResponse>> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        // Re-checked HERE, and not only when the worker built its candidate list: a payment that
        // lands in between has to win. Inside this handler the check shares a unit of work with
        // the cancel, and the order's xmin token closes the remaining gap — a payment committing
        // while we run makes SaveChanges conflict, ConcurrencyRetryBehavior clears the tracker and
        // re-runs, and the fresh read then fails this guard. Without it the sweeper could cancel
        // an order that was paid seconds earlier, releasing its stock and owing a refund.
        if (request.OnlyIfReservationExpired && !order.IsReservationExpired(DateTime.UtcNow))
            return OrderErrors.ReservationNotExpired(order.Id);

        // Capture BEFORE cancelling: past AwaitingRelease means preorder lines were
        // already converted to physical stock reservations (see ReleaseOrder).
        var preordersAlreadyBackedByStock = order.FulfillmentStatus
            is FulfillmentStatus.Paid or FulfillmentStatus.Preparing or FulfillmentStatus.PartiallyDelivered;

        var result = order.Cancel();
        if (result.IsFailure) return result.Error;

        foreach (var line in order.Lines)
        {
            var pending = line.QtyPending; // delivered units never come back
            if (pending <= 0) continue;

            if (line.IsPreorder && !preordersAlreadyBackedByStock)
            {
                // Still capacity-backed: give the slot back to the drop.
                var preorder = await db.Set<Preorder>()
                    .FirstOrDefaultAsync(p => p.ProductVariantId == line.ProductVariantId, ct);
                preorder?.ReleaseCapacity(pending);
            }
            else
            {
                // Stock-backed (plain stock line, or a released preorder line).
                var inventory = await db.Set<InventoryItem>()
                    .FirstOrDefaultAsync(i => i.ProductVariantId == line.ProductVariantId, ct);
                inventory?.Release(pending);
            }
        }

        return RegisterPaymentHandler.Map(order);
    }
}