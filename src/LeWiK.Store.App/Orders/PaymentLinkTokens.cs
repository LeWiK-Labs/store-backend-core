using LeWiK.Store.App.Common.Security;

namespace LeWiK.Store.App.Orders;

// Payment links are opaque bearer credentials like session tokens, so they share one
// implementation. Kept as a named entry point because the call sites read better for it,
// and because "what a payment link is" is worth being able to point at.
public static class PaymentLinkTokens
{
    public static string Generate() => OpaqueToken.Generate();
    public static string Hash(string token) => OpaqueToken.Hash(token);
}
