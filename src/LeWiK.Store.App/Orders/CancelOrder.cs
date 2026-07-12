using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record CancelOrderCommand(Guid OrderId) : ICommand<OrderPaymentResponse>;

public sealed class CancelOrderHandler(StoreDbContext db)
    : IRequestHandler<CancelOrderCommand, Result<OrderPaymentResponse>>
{
    public async Task<Result<OrderPaymentResponse>> Handle(CancelOrderCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        // Cancel first (the aggregate guards: can't cancel a delivered order).
        var result = order.Cancel();
        if (result.IsFailure) return result.Error;

        // Release each line's reservation back to stock or preorder capacity.
        foreach (var line in order.Lines)
        {
            var pending = line.QtyPending; // only what hasn't been handed over
            if (pending <= 0) continue;

            if (line.IsPreorder)
            {
                var preorder = await db.Set<Preorder>()
                    .FirstOrDefaultAsync(p => p.ProductVariantId == line.ProductVariantId, ct);
                preorder?.ReleaseCapacity(pending);
            }
            else
            {
                var inventory = await db.Set<InventoryItem>()
                    .FirstOrDefaultAsync(i => i.ProductVariantId == line.ProductVariantId, ct);
                inventory?.Release(pending);
            }
        }

        return RegisterPaymentHandler.Map(order);
    }
}