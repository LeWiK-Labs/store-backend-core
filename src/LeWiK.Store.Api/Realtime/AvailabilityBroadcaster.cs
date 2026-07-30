using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace LeWiK.Store.Api.Realtime;

// The payload the storefront receives; same shape for both ways of selling.
public sealed record AvailabilityUpdate(Guid VariantId, string Kind, int Available, bool IsSellable);

// Stock-backed variants: push on every stock movement.
public sealed class StockChangedBroadcaster(IHubContext<StoreHub> hub, ILogger<StockChangedBroadcaster> logger)
    : INotificationHandler<StockChanged>
{
    public Task Handle(StockChanged notification, CancellationToken ct) =>
        AvailabilityPush.SendAsync(hub, logger, notification.TenantId,
            new AvailabilityUpdate(notification.ProductVariantId, "Stock",
                notification.Available, notification.Available > 0), ct);
}

// Drop-backed variants: this is the live counter that matters during a drop.
public sealed class PreorderCapacityBroadcaster(IHubContext<StoreHub> hub, ILogger<PreorderCapacityBroadcaster> logger)
    : INotificationHandler<PreorderCapacityChanged>
{
    public Task Handle(PreorderCapacityChanged notification, CancellationToken ct) =>
        AvailabilityPush.SendAsync(hub, logger, notification.TenantId,
            new AvailabilityUpdate(notification.ProductVariantId, "Preorder",
                notification.AvailableCapacity, notification.IsSellable), ct);
}

internal static class AvailabilityPush
{
    // Domain events are dispatched by UnitOfWorkBehavior AFTER the commit, and MediatR lets an
    // exception from a handler out through the command. So a broken realtime path — Redis down,
    // a backplane hiccup — would surface as a 500 on a checkout whose order is already saved and
    // whose stock is already reserved: the buyer sees a failure and reorders. Nothing downstream
    // of the commit may fail the command, and a missed frame costs the page one stale number
    // until the next event or reload, which is exactly what a dropped connection already costs.
    public static async Task SendAsync(
        IHubContext<StoreHub> hub, ILogger logger, Guid tenantId, AvailabilityUpdate update, CancellationToken ct)
    {
        try
        {
            await hub.Clients
                .Group(StoreHub.VariantGroup(tenantId, update.VariantId))
                .SendAsync("availabilityChanged", update, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to broadcast availability for variant {Variant}", update.VariantId);
        }
    }
}
