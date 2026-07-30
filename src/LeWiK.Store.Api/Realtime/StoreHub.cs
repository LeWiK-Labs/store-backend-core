using LeWiK.Store.App.Platform;
using Microsoft.AspNetCore.SignalR;

namespace LeWiK.Store.Api.Realtime;

// Live availability for drop pages. Groups are tenant-scoped so a client can only
// ever receive updates for the store it connected to.
public sealed class StoreHub(StoreResolver resolver) : Hub
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

    // Subscribe to a variant's availability (drop page). The id is not validated against the
    // catalog on purpose: a variant of another store joins a group named after THIS tenant,
    // which nothing ever broadcasts to. Isolation comes from the group name, not from a check.
    public async Task WatchVariant(Guid variantId)
    {
        if (Context.Items[TenantKey] is not Guid tenantId) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, VariantGroup(tenantId, variantId));
    }

    public async Task UnwatchVariant(Guid variantId)
    {
        if (Context.Items[TenantKey] is not Guid tenantId) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, VariantGroup(tenantId, variantId));
    }
}
