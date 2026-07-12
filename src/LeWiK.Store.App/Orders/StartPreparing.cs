using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record StartPreparingCommand(Guid OrderId) : ICommand<OrderPaymentResponse>;

public sealed class StartPreparingHandler(StoreDbContext db)
    : IRequestHandler<StartPreparingCommand, Result<OrderPaymentResponse>>
{
    public async Task<Result<OrderPaymentResponse>> Handle(StartPreparingCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        var result = order.StartPreparing();
        return result.IsFailure ? result.Error : RegisterPaymentHandler.Map(order);
    }
}