using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog;

public sealed record GetPurchaseLimitQuery(PurchaseLimitScope Scope, Guid TargetId)
    : IQuery<PurchaseLimitResponse>;

public sealed class GetPurchaseLimitHandler(StoreDbContext db)
    : IRequestHandler<GetPurchaseLimitQuery, Result<PurchaseLimitResponse>>
{
    public async Task<Result<PurchaseLimitResponse>> Handle(GetPurchaseLimitQuery request, CancellationToken ct)
    {
        var limit = await db.Set<PurchaseLimit>()
            .Where(l => l.Scope == request.Scope && l.TargetId == request.TargetId)
            .Select(l => new PurchaseLimitResponse(
                l.Scope.ToString(), l.TargetId, l.MaxPerOrder, l.MaxPerCustomer, l.WindowDays))
            .FirstOrDefaultAsync(ct);

        return limit is null ? CatalogErrors.NoPurchaseLimit(request.TargetId) : limit;
    }
}