using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Storefront;

// Current availability of one variant. Used when a client subscribes to live
// updates, so it gets the present value and the stream in a single round-trip.
public sealed record GetVariantAvailabilityQuery(Guid ProductVariantId) : IQuery<StorefrontAvailability>;

public sealed class GetVariantAvailabilityHandler(StoreDbContext db)
    : IRequestHandler<GetVariantAvailabilityQuery, Result<StorefrontAvailability>>
{
    public async Task<Result<StorefrontAvailability>> Handle(
        GetVariantAvailabilityQuery request, CancellationToken ct)
    {
        // Same precedence as the composed read and as Checkout: an active drop wins over stock.
        // Both queries carry the tenant filter, so a variant of another store reads as stock 0
        // rather than leaking a number.
        var drop = await db.Set<Preorder>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProductVariantId == request.ProductVariantId
                                      && p.Status == PreorderStatus.Active, ct);
        if (drop is not null)
            return AvailabilityFactory.FromPreorder(drop);

        // Nullable projection so "no inventory row" and "a row holding zero" stay distinguishable
        // here; both end up as stock 0, which is what order.no_stock means to a buyer.
        var available = await db.Set<InventoryItem>().AsNoTracking()
            .Where(i => i.ProductVariantId == request.ProductVariantId)
            .Select(i => (int?)i.AvailableQuantity)
            .FirstOrDefaultAsync(ct);

        return AvailabilityFactory.FromStock(available ?? 0);
    }
}
