using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Customers;

public sealed record GetMyOrderQuery(Guid CustomerId, Guid OrderId) : IQuery<PaymentLinkOrderResponse>;

// Reuses the payment-link response: it is exactly the buyer's view of an order, and it was
// already trimmed for someone who must not see customer ids or gateway references.
public sealed class GetMyOrderHandler(StoreDbContext db)
    : IRequestHandler<GetMyOrderQuery, Result<PaymentLinkOrderResponse>>
{
    public async Task<Result<PaymentLinkOrderResponse>> Handle(GetMyOrderQuery request, CancellationToken ct)
    {
        // Scoped by customer AND id: asking for someone else's order is a 404, not a 403 —
        // a 403 would confirm that the order exists, which is itself worth knowing.
        var order = await db.Set<Order>()
            .Where(o => o.Id == request.OrderId && o.CustomerId == request.CustomerId)
            .Select(o => new PaymentLinkOrderResponse(
                o.Id, o.Currency, o.TotalAmount, o.PaidAmount, o.BalanceAmount,
                o.PaymentStatus.ToString(), o.ReservationExpiresAt,
                o.Lines.Select(l => new PaymentLinkLine(l.NameSnapshot, l.QtyOrdered, l.UnitPrice.Amount)).ToList()))
            .FirstOrDefaultAsync(ct);

        return order is null ? OrderErrors.OrderNotFound(request.OrderId) : order;
    }
}
