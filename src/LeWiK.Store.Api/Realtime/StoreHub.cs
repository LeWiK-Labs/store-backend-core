using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Storefront;
using MediatR;
using Microsoft.AspNetCore.SignalR;

namespace LeWiK.Store.Api.Realtime;

// Live availability for drop pages. Groups are tenant-scoped so a client can only
// ever receive updates for the store it connected to.
public sealed class StoreHub(StoreResolver resolver, ISender sender, TenantContext tenant) : Hub
{
    private const string TenantKey = "tenantId";

    public static string VariantGroup(Guid tenantId, Guid variantId) =>
        $"tenant:{tenantId}:variant:{variantId}";

    public override async Task OnConnectedAsync()
    {
        // Resolve the tenant from the connection's host, exactly like HTTP requests. Not read
        // from ITenantContext even though the middleware now fills it: SignalR builds its own
        // DI scope per hub call, so the one the middleware wrote to is not this one.
        // Read once and stash it: the client never gets to say which tenant it is.
        var host = Context.GetHttpContext()?.Request.Host.Host;
        var store = host is null ? null : await resolver.ResolveAsync(host);

        if (store is null || !store.IsActive)
        {
            Context.Abort();
            return;
        }

        Context.Items[TenantKey] = store.TenantId;
        await base.OnConnectedAsync();
    }

    // Subscribes AND returns the current value. Doing both here removes the race a caller would
    // otherwise have to avoid by hand: anything that changes between a separate read and the
    // subscription would be lost, leaving a stale counter with nothing ever coming to fix it.
    //
    // The order inside is what seals it — join the group FIRST, then read. A checkout landing
    // during the query already broadcasts to a group this connection is in, so the worst case is
    // seeing the same number twice, never missing it.
    //
    // The id is not validated against the catalog on purpose: a variant of another store joins a
    // group named after THIS tenant, which nothing ever broadcasts to, and the query below is
    // tenant-filtered so it reads back as stock 0. Isolation comes from the group name and the
    // filter, not from a check.
    public async Task<AvailabilityUpdate?> WatchVariant(Guid variantId)
    {
        if (Context.Items[TenantKey] is not Guid tenantId) return null;

        await Groups.AddToGroupAsync(Context.ConnectionId, VariantGroup(tenantId, variantId));

        // Hub invocations get their own DI scope, so the tenant middleware never touched
        // this TenantContext instance — pin it before querying.
        tenant.SetTenant(tenantId);

        var result = await sender.Send(new GetVariantAvailabilityQuery(variantId));
        if (result.IsFailure) return null;

        var a = result.Value;
        return new AvailabilityUpdate(variantId, a.Kind, a.Available, a.IsSellable);
    }

    public async Task UnwatchVariant(Guid variantId)
    {
        if (Context.Items[TenantKey] is not Guid tenantId) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, VariantGroup(tenantId, variantId));
    }
}
