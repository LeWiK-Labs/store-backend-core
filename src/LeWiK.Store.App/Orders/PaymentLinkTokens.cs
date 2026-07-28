using System.Security.Cryptography;
using System.Text;

namespace LeWiK.Store.App.Orders;

// Opaque payment-link tokens: random when issued, stored hashed, looked up by hash.
// Opaque on purpose — a JWT would carry claims nobody reads and, worse, could not be revoked.
public static class PaymentLinkTokens
{
    public static string Generate()
    {
        // 32 random bytes, url-safe. The token travels in a URL and is the ONLY thing standing
        // between a stranger and someone else's order, so guessing has to be hopeless.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Plain SHA-256 is the right call here, unlike for a password: the input is 256 bits of
    // randomness, so there is no dictionary to run and nothing a slow KDF would buy — while a
    // slow KDF WOULD cost a hash on every link click.
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
