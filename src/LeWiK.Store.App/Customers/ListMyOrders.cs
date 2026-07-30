using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Customers;

// CustomerId comes from the session claims, never from the request. The moment it could be
// supplied by the caller, "my orders" becomes "anyone's orders".
public sealed record ListMyOrdersQuery(Guid CustomerId) : IQuery<IReadOnlyList<MyOrderSummary>>;

public sealed record MyOrderSummary(
    Guid Id, DateTime CreatedAt, string FulfillmentStatus, string PaymentStatus,
    string Currency, decimal Total, decimal Paid, decimal Balance, int LineCount);

public sealed class ListMyOrdersHandler(StoreDbContext db)
    : IRequestHandler<ListMyOrdersQuery, Result<IReadOnlyList<MyOrderSummary>>>
{
    public async Task<Result<IReadOnlyList<MyOrderSummary>>> Handle(ListMyOrdersQuery request, CancellationToken ct)
    {
        var orders = await db.Set<Order>()
            .Where(o => o.CustomerId == request.CustomerId)   // tenant filter also applies
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new MyOrderSummary(
                o.Id, o.CreatedAt, o.FulfillmentStatus.ToString(), o.PaymentStatus.ToString(),
                o.Currency, o.TotalAmount, o.PaidAmount, o.BalanceAmount, o.Lines.Count))
            .ToListAsync(ct);

        return orders;
    }
}
