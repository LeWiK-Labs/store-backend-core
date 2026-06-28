using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Inventory.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Inventory;

public sealed record AddStockCommand(Guid ProductVariantId, int Quantity, string? Reason)
    : ICommand<StockLevelResponse>;

public sealed record StockLevelResponse(Guid ProductVariantId, int Available, int Reserved);

public sealed class AddStockValidator : AbstractValidator<AddStockCommand>
{
    public AddStockValidator()
    {
        RuleFor(x => x.ProductVariantId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class AddStockHandler(StoreDbContext db, ITenantContext tenant) : IRequestHandler<AddStockCommand, Result<StockLevelResponse>>
{
    public async Task<Result<StockLevelResponse>> Handle(AddStockCommand request, CancellationToken ct)
    {
        var item = await db.Set<InventoryItem>()
            .FirstOrDefaultAsync(i => i.ProductVariantId == request.ProductVariantId, ct);

        if (item is null)
        {
            item = new InventoryItem(tenant.TenantId, request.ProductVariantId);
            db.Add(item);
        }
        
        item.AddStock(request.Quantity, request.Reason);
        
        return new StockLevelResponse(item.ProductVariantId, item.AvailableQuantity, item.ReservedQuantity);
    }
}