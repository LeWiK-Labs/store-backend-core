using System.Security.Cryptography;
using System.Text;

namespace LeWiK.Store.App.Payments.Gateways;

// Validates MP's x-signature header (format: "ts=...,v1=..."), an HMAC-SHA256 over a
// manifest built from the notification's data.id, the x-request-id header and the timestamp.
// VERIFY the manifest format against Mercado Pago's current webhook docs before going to
// production — this is the one piece that could not be checked against the official source.
public static class MercadoPagoSignature
{
    public static bool IsValid(string? xSignature, string? xRequestId, string dataId, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return true;      // not configured: skip (integration)
        if (string.IsNullOrWhiteSpace(xSignature)) return false;

        var parts = xSignature.Split(',')
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        if (!parts.TryGetValue("ts", out var ts) || !parts.TryGetValue("v1", out var signature))
            return false;

        // MP lowercases alphanumeric ids in the manifest, and omits any segment whose value
        // is absent from the notification (an id-only notification has no request-id segment).
        var manifest = new StringBuilder($"id:{dataId.ToLowerInvariant()};");
        if (!string.IsNullOrWhiteSpace(xRequestId)) manifest.Append($"request-id:{xRequestId};");
        manifest.Append($"ts:{ts};");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest.ToString())))
            .ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(signature));
    }
}
