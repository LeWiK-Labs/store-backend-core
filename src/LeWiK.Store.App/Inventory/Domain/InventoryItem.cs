using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Inventory.Domain;

public enum StockMovementType { StockIn, Reserve, Release, Fulfill, Adjust }

public sealed class InventoryItem : AggregateRoot, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid ProductVariantId { get; private init; }
    public int AvailableQuantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<StockMovement> _movements = [];
    public IReadOnlyCollection<StockMovement> Movements => _movements.AsReadOnly();
    
    private InventoryItem() {} //EF

    public InventoryItem(Guid tenantId, Guid productVariantId)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        ProductVariantId = productVariantId;
    }

    public void AddStock(int quantity, string? reason = null)
    {
        RequirePositive(quantity);
        AvailableQuantity += quantity;
        Record(StockMovementType.StockIn, quantity, reason);
    }

    public Result Reserve(int quantity)
    {
        RequirePositive(quantity);
        if (quantity > AvailableQuantity) return InventoryErrors.InsufficientStock(quantity, AvailableQuantity);
        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
        Record(StockMovementType.Reserve, quantity);
        return Result.Success();
    }

    public void Release(int quantity)
    {
        RequirePositive(quantity);
        var qty = Math.Min(quantity, ReservedQuantity);
        ReservedQuantity -= qty;
        AvailableQuantity += qty;
        if (qty > 0) Record(StockMovementType.Release, qty);
    }

    public Result Fulfill(int quantity)
    {
        RequirePositive(quantity);
        if (quantity > ReservedQuantity) return InventoryErrors.InsufficientReserved(quantity, ReservedQuantity);
        ReservedQuantity -= quantity;
        Record(StockMovementType.Fulfill, quantity);
        return Result.Success();
    }

    private void Record(StockMovementType type, int quantity, string? reason = null)
    {
        _movements.Add(new StockMovement(Id, type, quantity, reason));
        Raise(new StockChanged(TenantId, ProductVariantId, AvailableQuantity, ReservedQuantity));
    }

    private static void RequirePositive(int quantity)
    {
        if(quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive");
    }
}