using LeWiK.Store.App.Preorders.Domain;

namespace LeWiK.Store.App.Storefront;

// Single definition of how availability is derived, shared by the storefront read
// and the realtime hub so they can never drift apart.
internal static class AvailabilityFactory
{
    public static StorefrontAvailability FromStock(int available) =>
        new("Stock", available, available > 0, null, null, null);

    public static StorefrontAvailability FromPreorder(Preorder drop) =>
        new("Preorder", drop.AvailableCapacity, drop.AvailableCapacity > 0,
            drop.ReleaseDate, drop.DepositType.ToString(), drop.DepositValue);
}
