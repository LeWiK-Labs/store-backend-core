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

        // Product-level limit (covers all variants)
        var productLimits = app.MapGroup("/products/{productId:guid}/purchase-limit");
        productLimits.MapPut("/", async (Guid productId, PurchaseLimitBody body, ISender sender) =>
            (await sender.Send(new ConfigurePurchaseLimitCommand(
                PurchaseLimitScope.Product, productId, body.MaxPerOrder, body.MaxPerCustomer, body.WindowDays)))
            .ToHttpResult());
        productLimits.MapGet("/", async (Guid productId, ISender sender) =>
            (await sender.Send(new GetPurchaseLimitQuery(PurchaseLimitScope.Product, productId))).ToHttpResult());

        // Variant-level limit (specific variant, e.g. the English version)
        var variantLimits = app.MapGroup("/variants/{variantId:guid}/purchase-limit");
        variantLimits.MapPut("/", async (Guid variantId, PurchaseLimitBody body, ISender sender) =>
            (await sender.Send(new ConfigurePurchaseLimitCommand(
                PurchaseLimitScope.Variant, variantId, body.MaxPerOrder, body.MaxPerCustomer, body.WindowDays)))
            .ToHttpResult());
        variantLimits.MapGet("/", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetPurchaseLimitQuery(PurchaseLimitScope.Variant, variantId))).ToHttpResult());
        
        return app;
    }
}

public sealed record PurchaseLimitBody(int? MaxPerOrder, int? MaxPerCustomer, int? WindowDays);