using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Orders.Domain;

public sealed class OrderLine : Entity
{
    public Guid OrderId { get; private init; }
    public Guid ProductVariantId { get; private init; }
    public string Sku { get; private init; } = null!;      // snapshot at order time
    public string NameSnapshot { get; private init; } = null!;
    public Money UnitPrice { get; private init; }           // snapshot at order time
    public int QtyOrdered { get; private init; }
    public int QtyFulfilled { get; private set; }
    public bool IsPreorder { get; private init; }

    public int QtyPending => QtyOrdered - QtyFulfilled;
    public bool IsFullyFulfilled => QtyFulfilled >= QtyOrdered;
    public Money LineTotal => UnitPrice.Multiply(QtyOrdered);
    
    private OrderLine() {} //EF
    
    internal OrderLine(Guid orderId, Guid productVariantId, string sku, string nameSnapshot,
        Money unitPrice, int qtyOrdered, bool isPreorder)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        ProductVariantId = productVariantId;
        Sku = sku;
        NameSnapshot = nameSnapshot;
        UnitPrice = unitPrice;
        QtyOrdered = qtyOrdered;
        QtyFulfilled = 0;
        IsPreorder = isPreorder;
    }

    // Hand over some units of this line. Returns failure if it exceeds what's pending.
    internal Result Fulfill(int qty)
    {
        if (qty <= 0)
            throw new ArgumentOutOfRangeException(nameof(qty), "Quantity must be positive.");
        if (qty > QtyPending)
            return OrderErrors.FulfillExceedsPending(Sku, qty, QtyPending);
        QtyFulfilled += qty;
        return Result.Success();
    }
}