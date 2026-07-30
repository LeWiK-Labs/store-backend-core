using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Preorders;
using LeWiK.Store.App.Preorders.Domain;
using MediatR;

namespace LeWiK.Store.Api.Preorders;

public static class PreorderEndpoints
{
    public static IEndpointRouteBuilder MapPreorderEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: the drop page shows remaining capacity — that is the whole hook of a drop.
        app.MapGet("/variants/{variantId:guid}/preorder", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetPreorderQuery(variantId))).ToHttpResult());

        // Admin: setting capacity, release date and deposit terms.
        app.MapPut("/admin/variants/{variantId:guid}/preorder",
                async (Guid variantId, ConfigurePreorderRequest body, ISender sender) =>
                    (await sender.Send(new ConfigurePreorderCommand(
                        variantId, body.Capacity, body.ReleaseDate, body.DepositType, body.DepositValue)))
                        .ToHttpResult())
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        // Admin: the drop is over. From here the variant sells from physical stock — which is the
        // only way back, since Checkout prefers an active preorder over stock unconditionally.
        app.MapPost("/admin/variants/{variantId:guid}/preorder/close",
                async (Guid variantId, ISender sender) =>
                    (await sender.Send(new ClosePreorderCommand(variantId))).ToHttpResult())
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        return app;
    }
}

public sealed record ConfigurePreorderRequest(
    int Capacity, DateTime ReleaseDate, DepositType DepositType, decimal DepositValue);
