using LeWiK.Store.App.Preorders.Domain;

namespace LeWiK.Store.App.Storefront;

// Single definition of how availability is derived, shared by the storefront read, the realtime
// hub and the broadcasters, so they can never drift apart. Precedence lives with the callers and
// is the same everywhere, including Checkout: an ACTIVE drop wins over stock, and every caller
// filters on that before reaching FromPreorder — a closed drop falls back to stock, which is why
// this method does not look at Status.
internal static class AvailabilityFactory
{
    public static StorefrontAvailability FromStock(int available) =>
        new("Stock", available, available > 0, null, null, null);

    public static StorefrontAvailability FromPreorder(Preorder drop) =>
        new("Preorder", drop.AvailableCapacity, drop.AvailableCapacity > 0,
            drop.ReleaseDate, drop.DepositType.ToString(), drop.DepositValue);
}
