using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Inventory.Domain;

public sealed record GetVariantStockQuery(Guid ProductVariantId) : IQuery<StockLevelResponse>;

public sealed class GetVariantStockHandler(StoreDbContext db) : IRequestHandler<GetVariantStockQuery, Result<StockLevelResponse>>
{
    public async Task<Result<StockLevelResponse>> Handle(GetVariantStockQuery request, CancellationToken ct)
    {
        var stock = await db.Set<InventoryItem>()
            .Where(i => i.ProductVariantId == request.ProductVariantId)
            .Select(i => new StockLevelResponse(i.ProductVariantId, i.AvailableQuantity, i.ReservedQuantity))
            .FirstOrDefaultAsync(ct);
        
        return stock is null ? InventoryErrors.NoInventory(request.ProductVariantId) : stock;
    }
}