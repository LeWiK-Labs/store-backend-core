using System.Security.Cryptography;
using System.Text;

namespace LeWiK.Store.App.Common.Security;

// Opaque credentials: random on issue, stored hashed, looked up by hash. One model for every
// bearer credential in the system — session tokens and payment links alike — so there is a
// single place to reason about how they are generated, stored and revoked.
public static class OpaqueToken
{
    public static string Generate()
    {
        // 32 random bytes, url-safe: a session cookie and a link path segment both carry this.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Plain SHA-256 is right here, unlike for a password: the input is 256 bits of randomness,
    // so there is no dictionary to run and nothing a slow KDF would buy — while a slow KDF
    // WOULD cost a hash on every single authenticated request.
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
