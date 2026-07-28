using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record GetOrderByPaymentLinkQuery(Guid OrderId) : IQuery<PaymentLinkOrderResponse>;

// Deliberately thinner than OrderResponse: whoever holds the link is unauthenticated, so this
// carries only what it takes to recognise the purchase and know what is owed. No customer id,
// no fulfillment detail, no gateway references.
public sealed record PaymentLinkOrderResponse(
    Guid OrderId, string Currency, decimal Total, decimal Paid, decimal Balance,
    string PaymentStatus, IReadOnlyList<PaymentLinkLine> Lines);

public sealed record PaymentLinkLine(string Name, int Quantity, decimal UnitPrice);

public sealed class GetOrderByPaymentLinkHandler(StoreDbContext db)
    : IRequestHandler<GetOrderByPaymentLinkQuery, Result<PaymentLinkOrderResponse>>
{
    public async Task<Result<PaymentLinkOrderResponse>> Handle(
        GetOrderByPaymentLinkQuery request, CancellationToken ct)
    {
        // No IgnoreQueryFilters here on purpose: the endpoint already pinned the tenant from
        // the token, so this runs under normal isolation like every other query.
        var order = await db.Set<Order>()
            .Where(o => o.Id == request.OrderId)
            .Select(o => new PaymentLinkOrderResponse(
                o.Id, o.Currency, o.TotalAmount, o.PaidAmount, o.BalanceAmount,
                o.PaymentStatus.ToString(),
                o.Lines.Select(l => new PaymentLinkLine(l.NameSnapshot, l.QtyOrdered, l.UnitPrice.Amount)).ToList()))
            .FirstOrDefaultAsync(ct);

        return order is null ? OrderErrors.OrderNotFound(request.OrderId) : order;
    }
}
