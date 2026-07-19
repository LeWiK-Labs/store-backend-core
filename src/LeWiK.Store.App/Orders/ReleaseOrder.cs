using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record ReleaseOrderCommand(Guid OrderId) : ICommand<OrderPaymentResponse>;

public sealed class ReleaseOrderHandler(StoreDbContext db)
    : IRequestHandler<ReleaseOrderCommand, Result<OrderPaymentResponse>>
{
    public async Task<Result<OrderPaymentResponse>> Handle(ReleaseOrderCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        // Guards: must be AwaitingRelease and fully paid.
        var result = order.MarkReleased();
        if (result.IsFailure) return result.Error;

        // Back each preorder line with physical stock. The drop's units must have
        // arrived and been restocked; reserving here mirrors what checkout does for
        // stock lines, so fulfillment later consumes a real reservation.
        // Preorder capacity (SoldCount) is NOT returned: the slot stays consumed.
        foreach (var line in order.Lines.Where(l => l.IsPreorder && l.QtyPending > 0))
        {
            var inventory = await db.Set<InventoryItem>()
                .FirstOrDefaultAsync(i => i.ProductVariantId == line.ProductVariantId, ct);
            if (inventory is null)
                return OrderErrors.PreorderStockMissing(line.Sku);

            var reserve = inventory.Reserve(line.QtyPending);
            if (reserve.IsFailure) return reserve.Error; // insufficient_stock = drop not restocked
        }

        return RegisterPaymentHandler.Map(order);
    }
}