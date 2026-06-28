using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Inventory.Domain;

public sealed class StockMovement : Entity
{
    public Guid InventoryId { get; private init; }
    public StockMovementType Type { get; private init; }
    public int Quantity { get; private init; }
    public string? Reason { get; private init; }
    public DateTime CreatedAt { get; private init; }
    
    private StockMovement() {} //EF

    internal StockMovement(Guid inventoryId, StockMovementType type, int quantity, string? reason)
    {
        Id = Guid.CreateVersion7();
        InventoryId = inventoryId;
        Type = type;
        Quantity = quantity;
        Reason = reason;
        CreatedAt = DateTime.UtcNow;
    }
}