using LeWiK.Store.App.Common.Results;

namespace LeWiK.Store.App.Preorders;

public static class PreorderErrors
{
    public static Error NotFound(Guid variantId) =>
        Error.NotFound("preorder.not_found", $"No preorder for variant {variantId}.");
    public static Error CapacityExceeded(int requested, int available) =>
        Error.Conflict("preorder.capacity_exceeded", $"Requested {requested} but only {available} left in the drop.");
    public static Error Closed() =>
        Error.Conflict("preorder.closed", "This preorder is closed.");
    public static Error AlreadyClosed() =>
        Error.Conflict("preorder.already_closed", "This preorder is already closed.");
    public static Error CapacityBelowSold(int capacity, int sold) =>
        Error.Validation("preorder.capacity_below_sold", $"Capacity {capacity} cannot be below already sold {sold}.");
}