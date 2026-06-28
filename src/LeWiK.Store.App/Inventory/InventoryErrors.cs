using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Inventory;

public static class InventoryErrors
{
    public static Error InsufficientStock(int requested, int available) =>
        Error.Conflict("inventory.insufficient_stock", $"Requested: {requested} but only available: {available} available");
    
    public static Error InsufficientReserved(int requested, int reserved) =>
        Error.Conflict("inventory.insufficient_reserved", $"Requested: {requested} but only {reserved} reserved");
}