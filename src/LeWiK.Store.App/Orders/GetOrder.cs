using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record GetOrderQuery(Guid OrderId) : IQuery<OrderResponse>;

public sealed record OrderResponse(
    Guid Id, Guid CustomerId, string FulfillmentStatus, string PaymentStatus,
    string Currency, decimal Total, decimal Paid, decimal Balance, decimal DepositDue,
    IReadOnlyList<OrderLineResponse> Lines);

public sealed record OrderLineResponse(
    Guid Id, Guid ProductVariantId, string Sku, string Name,
    decimal UnitPrice, int QtyOrdered, int QtyFulfilled, bool IsPreorder);

public sealed class GetOrderHandler(StoreDbContext db)
    : IRequestHandler<GetOrderQuery, Result<OrderResponse>>
{
    public async Task<Result<OrderResponse>> Handle(GetOrderQuery request, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Where(o => o.Id == request.OrderId)
            .Select(o => new OrderResponse(
                o.Id, o.CustomerId, o.FulfillmentStatus.ToString(), o.PaymentStatus.ToString(),
                o.Currency, o.TotalAmount, o.PaidAmount, o.BalanceAmount, o.DepositDueAmount,
                o.Lines.Select(l => new OrderLineResponse(
                    l.Id, l.ProductVariantId, l.Sku, l.NameSnapshot,
                    l.UnitPrice.Amount, l.QtyOrdered, l.QtyFulfilled, l.IsPreorder)).ToList()))
            .FirstOrDefaultAsync(ct);

        return order is null ? OrderErrors.OrderNotFound(request.OrderId) : order;
    }
}