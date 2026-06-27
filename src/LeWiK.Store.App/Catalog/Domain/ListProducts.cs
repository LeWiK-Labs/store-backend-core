using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed record ListProductsQuery() : IQuery<IReadOnlyList<ProductResponse>>;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string? Description,
    decimal PriceAmount,
    string PriceCurrency,
    string Status);

public sealed class ListProductsHandler(StoreDbContext db)
    : IRequestHandler<ListProductsQuery, Result<IReadOnlyList<ProductResponse>>>
{
    public async Task<Result<IReadOnlyList<ProductResponse>>> Handle(ListProductsQuery request, CancellationToken ct)
    {
        var products = await db.Set<Product>()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(
                p.Id, p.Sku, p.Name, p.Description,
                p.Price.Amount, p.Price.Currency, p.Status.ToString()))
            .ToListAsync(ct);

        return products;
    }
}