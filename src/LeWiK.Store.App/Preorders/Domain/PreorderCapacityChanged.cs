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
    // clock: a closed drop with 40 units left still refuses every order (preorder.closed). Without
    // it, closing one would push "40 available, go ahead" to everyone watching, and the counter
    // would contradict both the storefront read — which only composes active drops — and checkout.
    public bool IsSellable => IsOpen && AvailableCapacity > 0;
}
