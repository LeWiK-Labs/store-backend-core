using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Platform;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.Api.Tenancy;

// The store is identified by the domain the request arrived on, resolved against the Store
// table. Until 4.4 it came from a header the caller chose, which is only acceptable while
// nothing is protected.
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancySettings> settings,
    ILogger<TenantResolutionMiddleware> logger)
{
    // Paths that don't belong to a store, or that resolve the tenant themselves
    // (gateway callbacks and guest payment links carry it in a token or the path).
    // "/health" is deliberately NOT here: /health/tenant exists precisely to report what
    // this middleware resolved, and skipping it would make it always answer "no tenant".
    // "/hubs" came off this list in 4.7: the hub broadcasts per store now, so its connections
    // have to be resolved and suspension-checked like any other request.
    private static readonly string[] TenantlessPaths =
    [
        "/platform", "/pay/", "/auth/platform",
        "/payments/webpay/return", "/payments/mercadopago/webhook"
    ];

    // Reachable even when the store is suspended, so STAFF get an explanation instead of a
    // silent wall: login answers platform.store_suspended, and a live session can still ask
    // /auth/staff/me. /health stays up because a suspended store is not a broken one.
    //
    // Customer login is deliberately NOT here. Until 4.7 the allowlist was the bare "/auth"
    // prefix, which let buyers keep signing in to a store that answers 403 to everything else
    // — an asymmetry nobody decided. A suspended store is closed to buyers; the people who
    // need to see why are the ones who can do something about it.
    private static readonly string[] SuspendedAllowlist = ["/auth/staff", "/auth/platform", "/health"];

    public async Task InvokeAsync(HttpContext context, TenantContext tenant, StoreResolver resolver)
    {
        var path = context.Request.Path.Value ?? "";
        if (Matches(TenantlessPaths, path))
        {
            await next(context);
            return;
        }

        // Development override, gated by config (see the startup guard in Program.cs). It
        // returns early on purpose: the header names a tenant directly, so there is no host
        // to resolve and no cached status to consult. A suspended store therefore stays
        // operable through the header — acceptable because this never runs in production,
        // and the suspension cut-off is exercised over real hostnames instead.
        if (settings.Value.AllowHeaderOverride
            && context.Request.Headers.TryGetValue("X-Tenant-Id", out var header)
            && Guid.TryParse(header, out var headerTenant))
        {
            tenant.SetTenant(headerTenant);
            await next(context);
            return;
        }

        var store = await resolver.ResolveAsync(context.Request.Host.Host, context.RequestAborted);

        if (store is null)
        {
            // Unknown host: no tenant. Commands fail via TenantGuardBehavior, reads come
            // back empty — failing closed either way.
            logger.LogDebug("No store resolved for host {Host}", context.Request.Host.Host);
            await next(context);
            return;
        }

        tenant.SetTenant(store.TenantId);

        if (!store.IsActive && !Matches(SuspendedAllowlist, path))
        {
            await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "platform.store_suspended",
                    detail: "This store is suspended.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool Matches(string[] prefixes, string path) =>
        prefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
