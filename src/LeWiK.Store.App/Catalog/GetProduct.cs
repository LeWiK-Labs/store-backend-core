using LeWiK.Store.App.Catalog.Domain;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Catalog;

public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductResponse>;

public sealed class GetProductHandler(StoreDbContext db)
    : IRequestHandler<GetProductQuery, Result<ProductResponse>>
{
    public async Task<Result<ProductResponse>> Handle(GetProductQuery request, CancellationToken ct)
    {
        var product = await db.Set<Product>()
            .Where(p => p.Id == request.ProductId)
            .Select(ProductProjection.ToResponse)
            .FirstOrDefaultAsync(ct);

        return product is null ? CatalogErrors.ProductNotFound(request.ProductId) : product;
    }
}