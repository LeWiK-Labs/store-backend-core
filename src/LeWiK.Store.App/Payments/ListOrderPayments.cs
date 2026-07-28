using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Payments;

// Refunds target a charge, so the store has to see the charges to pick one. An order paid with
// a deposit plus a balance has two, each refundable on its own.
public sealed record ListOrderPaymentsQuery(Guid OrderId) : IQuery<IReadOnlyList<PaymentListItem>>;

public sealed record PaymentListItem(
    Guid Id, string Gateway, string Type, decimal Amount, string Currency,
    string State, string? ExternalReference, decimal RefundedAmount, DateTime CreatedAt);

public sealed class ListOrderPaymentsHandler(StoreDbContext db)
    : IRequestHandler<ListOrderPaymentsQuery, Result<IReadOnlyList<PaymentListItem>>>
{
    public async Task<Result<IReadOnlyList<PaymentListItem>>> Handle(
        ListOrderPaymentsQuery request, CancellationToken ct)
    {
        var payments = await db.Set<Payment>()
            .Where(p => p.OrderId == request.OrderId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PaymentListItem(
                p.Id, p.Gateway.ToString(), p.Type.ToString(), p.Amount, p.Currency,
                p.State.ToString(), p.ExternalReference,
                db.Set<Refund>()
                    .Where(r => r.PaymentId == p.Id && r.State == RefundState.Succeeded)
                    .Sum(r => (decimal?)r.Amount) ?? 0m,
                p.CreatedAt))
            .ToListAsync(ct);

        return payments;
    }
}
