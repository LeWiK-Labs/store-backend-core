using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
// "Store" alone binds to the LeWiK.Store namespace from inside LeWiK.Store.App.*, so the entity
// always needs qualifying.
using StoreEntity = LeWiK.Store.App.Platform.Domain.Store;

namespace LeWiK.Store.App.Storefront;

public sealed record GetStorefrontQuery() : IQuery<StorefrontResponse>;

public sealed record StorefrontResponse(StoreInfo Store, IReadOnlyList<StorefrontProduct> Products);

public sealed record StoreInfo(string Name, string Slug);

public sealed record StorefrontProduct(
    Guid Id, string Name, string? Description,
    IReadOnlyList<StorefrontOption> Options,
    IReadOnlyList<StorefrontVariant> Variants,
    StorefrontLimit? Limit);

public sealed record StorefrontOption(string Name, IReadOnlyList<StorefrontOptionValue> Values);
public sealed record StorefrontOptionValue(Guid Id, string Value);

public sealed record StorefrontVariant(
    Guid Id, string Sku, string Label, decimal Price, string Currency,
    IReadOnlyList<Guid> OptionValueIds,
    StorefrontAvailability Availability,
    StorefrontLimit? Limit);

// One shape for both ways of selling: "Stock" or "Preorder". The front renders a number and a
// buy button; which table it came from is our problem, not the page's.
public sealed record StorefrontAvailability(
    string Kind, int Available, bool IsSellable,
    DateTime? ReleaseDate, string? DepositType, decimal? DepositValue);

// Only the per-order cap is public: the storefront needs it to cap the quantity
// selector, and a buyer discovers it anyway by asking for too many. MaxPerCustomer
// and WindowDays stay admin-only — publishing them hands a scalper the exact
// recipe (how many accounts to make, how often to rotate them).
public sealed record StorefrontLimit(int? MaxPerOrder);

// Composes catalog + availability + limits in one read: the storefront can't afford
// N+1 calls. Modules stay separate for writes; this is a presentation-layer read.
public sealed class GetStorefrontHandler(StoreDbContext db, ITenantContext tenant)
    : IRequestHandler<GetStorefrontQuery, Result<StorefrontResponse>>
{
    public async Task<Result<StorefrontResponse>> Handle(GetStorefrontQuery request, CancellationToken ct)
    {
        var store = await db.Set<StoreEntity>().AsNoTracking()
            .Where(s => s.Id == tenant.TenantId)
            .Select(s => new StoreInfo(s.Name, s.Slug))
            .FirstOrDefaultAsync(ct);
        // An unresolved host leaves TenantId empty, which matches no store: the storefront of
        // nowhere is a 404, not an empty catalog that looks like a store with nothing to sell.
        if (store is null) return PlatformErrors.StoreNotFound(tenant.TenantId);

        // Active products with their options and variants (tenant filter applies).
        var products = await db.Set<Product>().AsNoTracking()
            .Where(p => p.Status == ProductStatus.Active)
            .OrderBy(p => p.Name)
            .Select(p => new
            {
                p.Id, p.Name, p.Description,
                Options = p.Options.OrderBy(o => o.Position).Select(o => new StorefrontOption(
                    o.Name,
                    o.Values.OrderBy(v => v.Position).Select(v => new StorefrontOptionValue(v.Id, v.Value)).ToList()
                )).ToList(),
                Variants = p.Variants.Select(v => new
                {
                    v.Id, v.Sku, v.Label,
                    Amount = v.Price.Amount, Currency = v.Price.Currency,
                    OptionValueIds = v.OptionValues.Select(ov => ov.ProductOptionValueId).ToList()
                }).ToList()
            })
            .ToListAsync(ct);

        var variantIds = products.SelectMany(p => p.Variants.Select(v => v.Id)).ToList();

        // Availability and limits in three flat lookups instead of per-variant queries. Bounded
        // by the catalog, not by it: five round trips for any number of products.
        var stock = await db.Set<InventoryItem>().AsNoTracking()
            .Where(i => variantIds.Contains(i.ProductVariantId))
            .ToDictionaryAsync(i => i.ProductVariantId, i => i.AvailableQuantity, ct);

        var preorders = await db.Set<Preorder>().AsNoTracking()
            .Where(p => variantIds.Contains(p.ProductVariantId) && p.Status == PreorderStatus.Active)
            .ToDictionaryAsync(p => p.ProductVariantId, p => p, ct);

        // Read whole and published narrow: only MaxPerOrder survives Map. A policy that is only
        // per-customer therefore leaves maxPerOrder null, so the front caps nothing and the limit
        // still applies at checkout — the cap exists, it just is not announced.
        var limits = await db.Set<PurchaseLimit>().AsNoTracking().ToListAsync(ct);
        var productLimits = limits.Where(l => l.Scope == PurchaseLimitScope.Product)
            .ToDictionary(l => l.TargetId, Map);
        var variantLimits = limits.Where(l => l.Scope == PurchaseLimitScope.Variant)
            .ToDictionary(l => l.TargetId, Map);

        var result = products.Select(p => new StorefrontProduct(
            p.Id, p.Name, p.Description, p.Options,
            p.Variants.Select(v => new StorefrontVariant(
                v.Id, v.Sku, v.Label, v.Amount, v.Currency, v.OptionValueIds,
                Availability(v.Id, stock, preorders),
                variantLimits.GetValueOrDefault(v.Id))).ToList(),
            productLimits.GetValueOrDefault(p.Id))).ToList();

        return new StorefrontResponse(store, result);
    }

    // A variant with an active preorder sells against capacity; otherwise from stock. Same order
    // of precedence as Checkout, which is the point — the page must not offer what the checkout
    // would refuse. A variant with neither reads as stock 0, which is what order.no_stock means.
    private static StorefrontAvailability Availability(
        Guid variantId, Dictionary<Guid, int> stock, Dictionary<Guid, Preorder> preorders) =>
        preorders.TryGetValue(variantId, out var drop)
            ? AvailabilityFactory.FromPreorder(drop)
            : AvailabilityFactory.FromStock(stock.GetValueOrDefault(variantId));

    private static StorefrontLimit Map(PurchaseLimit l) => new(l.MaxPerOrder);
}
