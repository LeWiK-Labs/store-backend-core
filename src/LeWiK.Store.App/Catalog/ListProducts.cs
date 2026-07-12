using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace LeWiK.Store.App.Catalog;

public sealed record ListProductsQuery() : IQuery<IReadOnlyList<ProductResponse>>;

public sealed record ProductResponse(
    Guid Id, string Name, string? Description, string Status,
    IReadOnlyList<OptionResponse> Options,
    IReadOnlyList<VariantResponse> Variants);

public sealed record OptionResponse(string Name, IReadOnlyList<OptionValueResponse> Values);
public sealed record OptionValueResponse(Guid Id, string Value);

public sealed record VariantResponse(
    Guid Id, string Sku, string Label, decimal PriceAmount, string PriceCurrency,
    IReadOnlyList<Guid> OptionValueIds);

public sealed class ListProductsHandler(StoreDbContext db)
    : IRequestHandler<ListProductsQuery, Result<IReadOnlyList<ProductResponse>>>
{
    public async Task<Result<IReadOnlyList<ProductResponse>>> Handle(ListProductsQuery request, CancellationToken ct)
    {
        var products = await db.Set<Product>()
            .OrderBy(p => p.Name)
            .Select(ProductProjection.ToResponse)
            .ToListAsync(ct);

        return products;
    }
}

internal static class ProductProjection
{
    public static readonly Expression<Func<Product, ProductResponse>> ToResponse = p => new ProductResponse(
        p.Id,
        p.Name,
        p.Description,
        p.Status.ToString(),
        p.Options
            .OrderBy(o => o.Position)
            .Select(o => new OptionResponse(
                o.Name,
                o.Values.OrderBy(v => v.Position)
                    .Select(v => new OptionValueResponse(v.Id, v.Value)).ToList()))
            .ToList(),
        p.Variants
            .Select(v => new VariantResponse(
                v.Id, v.Sku, v.Label, v.Price.Amount, v.Price.Currency,
                v.OptionValues.Select(ov => ov.ProductOptionValueId).ToList()))
            .ToList());
}