using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Preorders.Domain;

// Raised whenever a drop's capacity, sold count or status moves. Drives the live counter
// on the storefront drop page.
public sealed record PreorderCapacityChanged(
    Guid TenantId,
    Guid ProductVariantId,
    int Capacity,
    int SoldCount,
    bool IsOpen) : IDomainEvent
{
    public int AvailableCapacity => Capacity - SoldCount;

    // IsOpen is carried, and not just the numbers, because closing a drop leaves capacity on the
    // clock: a closed drop with 40 units left still refuses every order (preorder.closed), so the
    // numbers alone do not say what happened.
    //
    // It is not turned into a "sellable" flag here. Whether a variant can be bought is derived by
    // AvailabilityFactory from the variant's whole situation — an active drop wins over stock,
    // a closed one falls back to it — and one definition of that is the point.
}
