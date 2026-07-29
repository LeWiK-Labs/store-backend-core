using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Catalog;
using LeWiK.Store.App.Catalog.Domain;
using MediatR;

namespace LeWiK.Store.Api.Catalog;

public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        // --- Public: the storefront browses the catalog anonymously ---
        var catalog = app.MapGroup("/products");

        catalog.MapGet("/", async (ISender sender) =>
            (await sender.Send(new ListProductsQuery())).ToHttpResult());
        catalog.MapGet("/{productId:guid}", async (Guid productId, ISender sender) =>
            (await sender.Send(new GetProductQuery(productId))).ToHttpResult());

        // --- Admin: managing the catalog ---
        // The URL carries the security boundary, so a route that is missing its protection is
        // visible as a route in the wrong group rather than as an absent attribute.
        var admin = app.MapGroup("/admin/products")
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        admin.MapPost("/", async (CreateProductCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());
        admin.MapPost("/with-options", async (CreateProductWithOptionsCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        // Purchase limits are anti-scalping policy: reading them tells a scalper exactly what
        // to stay under, so they live behind the panel with the rest of the configuration.
        admin.MapPut("/{productId:guid}/purchase-limit", async (Guid productId, PurchaseLimitBody body, ISender sender) =>
            (await sender.Send(new ConfigurePurchaseLimitCommand(
                PurchaseLimitScope.Product, productId, body.MaxPerOrder, body.MaxPerCustomer, body.WindowDays)))
                .ToHttpResult());
        admin.MapGet("/{productId:guid}/purchase-limit", async (Guid productId, ISender sender) =>
            (await sender.Send(new GetPurchaseLimitQuery(PurchaseLimitScope.Product, productId))).ToHttpResult());

        var adminVariants = app.MapGroup("/admin/variants")
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        adminVariants.MapPut("/{variantId:guid}/purchase-limit", async (Guid variantId, PurchaseLimitBody body, ISender sender) =>
            (await sender.Send(new ConfigurePurchaseLimitCommand(
                PurchaseLimitScope.Variant, variantId, body.MaxPerOrder, body.MaxPerCustomer, body.WindowDays)))
                .ToHttpResult());
        adminVariants.MapGet("/{variantId:guid}/purchase-limit", async (Guid variantId, ISender sender) =>
            (await sender.Send(new GetPurchaseLimitQuery(PurchaseLimitScope.Variant, variantId))).ToHttpResult());

        return app;
    }
}

public sealed record PurchaseLimitBody(int? MaxPerOrder, int? MaxPerCustomer, int? WindowDays);
