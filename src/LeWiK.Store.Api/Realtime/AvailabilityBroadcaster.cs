using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Preorders.Domain;
using LeWiK.Store.App.Storefront;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace LeWiK.Store.Api.Realtime;

// The payload the storefront receives; same shape for both ways of selling.
public sealed record AvailabilityUpdate(Guid VariantId, string Kind, int Available, bool IsSellable);

// Stock-backed variants: push on every stock movement.
public sealed class StockChangedBroadcaster(
    IHubContext<StoreHub> hub, ISender sender, ITenantContext tenant, ILogger<StockChangedBroadcaster> logger)
    : INotificationHandler<StockChanged>
{
    public Task Handle(StockChanged notification, CancellationToken ct) =>
        AvailabilityPush.SendAsync(hub, sender, tenant, logger,
            notification.TenantId, notification.ProductVariantId, ct);
}

// Drop-backed variants: this is the live counter that matters during a drop.
public sealed class PreorderCapacityBroadcaster(
    IHubContext<StoreHub> hub, ISender sender, ITenantContext tenant, ILogger<PreorderCapacityBroadcaster> logger)
    : INotificationHandler<PreorderCapacityChanged>
{
    public Task Handle(PreorderCapacityChanged notification, CancellationToken ct) =>
        AvailabilityPush.SendAsync(hub, sender, tenant, logger,
            notification.TenantId, notification.ProductVariantId, ct);
}

internal static class AvailabilityPush
{
    // Both handlers say only WHICH variant moved, never what to show. The event knows what
    // changed; only a read knows how the variant currently sells. An active drop wins over stock
    // — Checkout, the composed read and WatchVariant all agree on that — so building the payload
    // from the event pushed "Stock 30" to a page showing "Preorder 50" the moment the drop's
    // merchandise was loaded: the counter changed unit mid-flight, and a reload contradicted it.
    // Asking for current availability keeps that precedence in one place, the one the reads use.
    //
    // The cost is one indexed lookup per movement. The alternative was a second definition of
    // "available", which is how a page ends up promising what the checkout refuses.
    //
    // Domain events are dispatched by UnitOfWorkBehavior AFTER the commit, and MediatR lets an
    // exception from a handler out through the command. So a broken realtime path — Redis down,
    // a backplane hiccup, and now this query too — would surface as a 500 on a checkout whose
    // order is already saved and whose stock is already reserved: the buyer sees a failure and
    // reorders. Nothing downstream of the commit may fail the command, and a missed frame costs
    // the page one stale number until the next event or reload, which is exactly what a dropped
    // connection already costs.
    public static async Task SendAsync(
        IHubContext<StoreHub> hub, ISender sender, ITenantContext tenant, ILogger logger,
        Guid tenantId, Guid variantId, CancellationToken ct)
    {
        try
        {
            // The query reads through the tenant filter, which follows the ambient context, while
            // the group is named after the event's tenant. Every path that gets here pins them to
            // the same store (a request, a hub call, one sweeper scope per order); if they ever
            // came apart, the read would come back empty and we would broadcast "sold out" to a
            // store that is not sold out. Say nothing instead.
            if (tenant.TenantId != tenantId)
            {
                logger.LogWarning(
                    "Skipping availability broadcast: ambient tenant {Ambient} does not match event tenant {Event}",
                    tenant.TenantId, tenantId);
                return;
            }

            var result = await sender.Send(new GetVariantAvailabilityQuery(variantId), ct);
            if (result.IsFailure) return;

            var a = result.Value;
            await hub.Clients
                .Group(StoreHub.VariantGroup(tenantId, variantId))
                .SendAsync("availabilityChanged",
                    new AvailabilityUpdate(variantId, a.Kind, a.Available, a.IsSellable), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to broadcast availability for variant {Variant}", variantId);
        }
    }
}
