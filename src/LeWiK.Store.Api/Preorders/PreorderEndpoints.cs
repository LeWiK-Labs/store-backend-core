using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Preorders;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;

namespace LeWiK.Store.Api.Preorders;

public static class PreorderEndpoints
{
    public static IEndpointRouteBuilder MapPreorderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/variants/{variantId:guid}/preorder");

        group.MapPut("/", async (Guid variantId, ConfigurePreorderRequest body, ISender sender) =>
            (await sender.Send(new ConfigurePreorderCommand(
                variantId, body.Capacity, body.ReleaseDate, body.DepositType, body.DepositValue)))
            .ToHttpResult());

        group.MapGet("/", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetPreorderQuery(variantId))).ToHttpResult());

        return app;
    }
}

public sealed record ConfigurePreorderRequest(
    int Capacity, DateTime ReleaseDate, DepositType DepositType, decimal DepositValue);