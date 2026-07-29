using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Inventory;
using LeWiK.Store.App.Inventory.Domain;
using MediatR;

namespace LeWiK.Store.Api.Inventory;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: the storefront has to show whether something is in stock.
        app.MapGet("/variants/{variantId:guid}/stock", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetVariantStockQuery(variantId))).ToHttpResult());

        // Admin: restocking. Reading availability is a shop window; changing it is not.
        app.MapPost("/admin/variants/{variantId:guid}/stock",
                async (Guid variantId, AddStockRequest body, ISender sender) =>
                    (await sender.Send(new AddStockCommand(variantId, body.Quantity, body.Reason))).ToHttpResult())
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        return app;
    }
}

public sealed record AddStockRequest(int Quantity, string? Reason);
