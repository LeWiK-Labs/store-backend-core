using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
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
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        var result = order.MarkReleased();
        return result.IsFailure ? result.Error : RegisterPaymentHandler.Map(order);
    }
}