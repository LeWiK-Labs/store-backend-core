using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Storefront;
using MediatR;

namespace LeWiK.Store.Api.Storefront;

public static class StorefrontEndpoints
{
    public static IEndpointRouteBuilder MapStorefrontEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: everything the storefront needs to render, resolved by domain.
        app.MapGet("/storefront", async (ISender sender) =>
            (await sender.Send(new GetStorefrontQuery())).ToHttpResult());

        return app;
    }
}
