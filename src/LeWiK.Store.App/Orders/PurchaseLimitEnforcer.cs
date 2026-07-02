using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

// What the customer wants to buy, flattened for limit checks.
public sealed record PurchaseIntent(Guid ProductId, Guid ProductVariantId, int Quantity);

// Enforces per-product and per-variant purchase limits against the customer's history.
// A domain service: the Order aggregate stays free of cross-order queries.
public sealed class PurchaseLimitEnforcer(StoreDbContext db)
{
    public async Task<Result> CheckAsync(Guid customerId, IReadOnlyList<PurchaseIntent> intents, CancellationToken ct)
    {
        // Gather the product and variant ids in this cart.
        var productIds = intents.Select(i => i.ProductId).Distinct().ToList();
        var variantIds = intents.Select(i => i.ProductVariantId).Distinct().ToList();

        // Load only the policies that apply to what's being bought (tenant filter applies).
        var limits = await db.Set<PurchaseLimit>()
            .Where(l =>
                (l.Scope == PurchaseLimitScope.Product && productIds.Contains(l.TargetId)) ||
                (l.Scope == PurchaseLimitScope.Variant && variantIds.Contains(l.TargetId)))
            .ToListAsync(ct);

        if (limits.Count == 0)
            return Result.Success();

        foreach (var limit in limits)
        {
            // Quantity in THIS cart that the policy governs.
            var cartQty = limit.Scope == PurchaseLimitScope.Product
                ? intents.Where(i => i.ProductId == limit.TargetId).Sum(i => i.Quantity)
                : intents.Where(i => i.ProductVariantId == limit.TargetId).Sum(i => i.Quantity);

            if (cartQty == 0) continue;

            // Per-order cap: this cart alone can't exceed it.
            if (limit.MaxPerOrder.HasValue && cartQty > limit.MaxPerOrder.Value)
                return LimitError(limit, "per_order", limit.MaxPerOrder.Value);

            // Per-customer cap: cart + prior purchases within the window can't exceed it.
            if (limit.MaxPerCustomer.HasValue)
            {
                var alreadyBought = await CountHistoryAsync(customerId, limit, ct);
                if (alreadyBought + cartQty > limit.MaxPerCustomer.Value)
                    return LimitError(limit, "per_customer", limit.MaxPerCustomer.Value);
            }
        }

        return Result.Success();
    }

    // Sum of units this customer already ordered under the policy, within its window,
    // excluding cancelled orders.
    private async Task<int> CountHistoryAsync(Guid customerId, PurchaseLimit limit, CancellationToken ct)
    {
        var since = limit.WindowDays.HasValue
            ? DateTime.UtcNow.AddDays(-limit.WindowDays.Value)
            : (DateTime?)null;

        // Query order lines belonging to this customer's non-cancelled orders.
        var query =
            from o in db.Set<Order>()
            where o.CustomerId == customerId && o.FulfillmentStatus != FulfillmentStatus.Cancelled
            where since == null || o.CreatedAt >= since
            from line in o.Lines
            where limit.Scope == PurchaseLimitScope.Product
                ? line.ProductId == limit.TargetId
                : line.ProductVariantId == limit.TargetId
            select line.QtyOrdered;

        return await query.SumAsync(ct);
    }

    private static Result LimitError(PurchaseLimit limit, string kind, int max)
    {
        var scope = limit.Scope.ToString().ToLowerInvariant();
        return Error.Conflict(
            $"order.limit_{kind}",
            $"Purchase limit reached: max {max} per {kind.Replace('_', ' ')} for this {scope}.");
    }
}