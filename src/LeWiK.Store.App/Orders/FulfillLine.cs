using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Inventory.Domain;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record FulfillLineCommand(Guid OrderId, Guid OrderLineId, int Quantity)
    : ICommand<OrderFulfillmentResponse>;

public sealed record OrderFulfillmentResponse(
    Guid OrderId, string FulfillmentStatus,
    IReadOnlyList<LineFulfillmentResponse> Lines);

public sealed record LineFulfillmentResponse(Guid LineId, string Sku, int Ordered, int Fulfilled);

public sealed class FulfillLineValidator : AbstractValidator<FulfillLineCommand>
{
    public FulfillLineValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.OrderLineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public sealed class FulfillLineHandler(StoreDbContext db)
    : IRequestHandler<FulfillLineCommand, Result<OrderFulfillmentResponse>>
{
    public async Task<Result<OrderFulfillmentResponse>> Handle(FulfillLineCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        var line = order.Lines.FirstOrDefault(l => l.Id == request.OrderLineId);
        if (line is null) return OrderErrors.LineNotFound(request.OrderLineId);

        // Consume the physical reservation for this line's variant.
        // (Stock lines were reserved at checkout; preorder lines get stock on release —
        //  simplified here: we consume reserved stock for the fulfilled quantity.)
        var inventory = await db.Set<InventoryItem>()
            .FirstOrDefaultAsync(i => i.ProductVariantId == line.ProductVariantId, ct);
        if (inventory is not null)
        {
            var consume = inventory.Fulfill(request.Quantity);
            if (consume.IsFailure) return consume.Error;
        }

        // The order aggregate updates the line and derives Delivered/PartiallyDelivered.
        var result = order.FulfillLine(request.OrderLineId, request.Quantity);
        if (result.IsFailure) return result.Error;

        return new OrderFulfillmentResponse(
            order.Id, order.FulfillmentStatus.ToString(),
            order.Lines.Select(l => new LineFulfillmentResponse(l.Id, l.Sku, l.QtyOrdered, l.QtyFulfilled)).ToList());
    }
}