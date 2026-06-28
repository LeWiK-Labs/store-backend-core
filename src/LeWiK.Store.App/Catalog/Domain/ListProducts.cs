using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed record ListProductsQuery() : IQuery<IReadOnlyList<ProductResponse>>;

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    IReadOnlyList<VariantResponse> Variants);

public sealed record VariantResponse(
    Guid Id,
    string Sku,
    string Label,
    decimal PriceAmount,
    string PriceCurrency);

public sealed class ListProductsHandler(StoreDbContext db)
    : IRequestHandler<ListProductsQuery, Result<IReadOnlyList<ProductResponse>>>
{
    public async Task<Result<IReadOnlyList<ProductResponse>>> Handle(ListProductsQuery request, CancellationToken ct)
    {
        var products = await db.Set<Product>()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(
                p.Id, 
                p.Name, 
                p.Description, 
                p.Status.ToString(), 
                p.Variants.Select(v=> 
                    new VariantResponse(
                        v.Id, 
                        v.Sku, 
                        v.Label, 
                        v.Price.Amount, 
                        v.Price.Currency)
                ).ToList()))
            .ToListAsync(ct);

        return products;
    }
}