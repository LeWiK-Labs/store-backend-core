using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.Api.Orders;

// The guest storefront: no tenant header, no account, no auth. Holding the token IS the
// authorisation, and it only authorises two things — seeing this order and paying its balance.
public static class PaymentLinkEndpoints
{
    public static IEndpointRouteBuilder MapPaymentLinkEndpoints(this IEndpointRouteBuilder app)
    {
        // Every failure answers the same 404 whatever went wrong — unknown token, expired,
        // cancelled order. Distinguishing them would tell a stranger probing tokens which
        // guesses were close.
        static IResult Invalid() => Result.Failure(OrderErrors.PaymentLinkInvalid()).ToHttpResult();

        app.MapGet("/pay/{token}", async (
            string token, TenantContext tenant, StoreDbContext db, ISender sender) =>
        {
            var order = await ResolveAsync(token, tenant, db);
            return order is null
                ? Invalid()
                : (await sender.Send(new GetOrderByPaymentLinkQuery(order.Id))).ToHttpResult();
        });

        // Pay the balance with the gateway the buyer picked. Reuses the ordinary initiation
        // path — the link changes who may ask, not what happens.
        app.MapPost("/pay/{token}/initiate", async (
            string token, PayLinkBody body, TenantContext tenant, StoreDbContext db, ISender sender) =>
        {
            var order = await ResolveAsync(token, tenant, db);
            if (order is null) return Invalid();

            return (await sender.Send(new InitiatePaymentCommand(order.Id, body.Gateway, PaymentType.Balance)))
                .ToHttpResult();
        });

        return app;
    }

    // Finds the order across tenants by token hash, then pins the tenant for the rest of the
    // request. Second and last intended use of IgnoreQueryFilters (the first is Webpay's
    // browser return): both are entry points where the caller cannot tell us which store.
    private static async Task<Order?> ResolveAsync(string token, TenantContext tenant, StoreDbContext db)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var hash = PaymentLinkTokens.Hash(token);

        var order = await db.Set<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.PaymentLinkHash == hash);

        if (order is null || !order.IsPaymentLinkValid(DateTime.UtcNow))
            return null;

        tenant.SetTenant(order.TenantId);   // from here on, normal isolation
        return order;
    }
}

public sealed record PayLinkBody(PaymentGateway Gateway);
