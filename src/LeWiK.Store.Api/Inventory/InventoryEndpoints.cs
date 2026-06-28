using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Inventory;
using LeWiK.Store.App.Inventory.Domain;
using MediatR;

namespace LeWiK.Store.Api.Inventory;

public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/variants/{variantId:guid}/stock");

        group.MapPost("/", async (Guid variantId, AddStockRequest body, ISender sender) =>
            (await sender.Send(new AddStockCommand(variantId, body.Quantity, body.Reason))).ToHttpResult());

        group.MapGet("/", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetVariantStockQuery(variantId))).ToHttpResult());

        return app;
    }
}

public sealed record AddStockRequest(int Quantity, string? Reason);