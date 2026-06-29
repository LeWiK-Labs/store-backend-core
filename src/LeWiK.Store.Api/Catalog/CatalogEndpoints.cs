using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Catalog;
using LeWiK.Store.App.Catalog.Domain;
using MediatR;

namespace LeWiK.Store.Api.Catalog;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/products");

        group.MapPost("/",
            async (CreateProductCommand command, ISender sender) => (await sender.Send(command)).ToHttpResult());
        
        group.MapPost("/with-options", async (CreateProductWithOptionsCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        group.MapGet("/", async (ISender sender) => (await sender.Send(new ListProductsQuery())).ToHttpResult());

        return app;
    }
}