using System.Security.Cryptography;
using System.Text;
using LeWiK.Store.App.Payments.Gateways;

namespace LeWiK.Tienda.Tests.Payments;

// The webhook is a public URL: without a valid signature anyone who guesses it could make us
// chase arbitrary payment ids. These tests pin the manifest format so that verifying it against
// Mercado Pago's docs before production is a diff, not a re-reading of the implementation.
public class MercadoPagoSignatureTests
{
    private const string Secret = "a-store-webhook-secret";

    // The documented manifest, spelled out here rather than reused from the implementation.
    private static string Sign(string manifest, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
    }

    private static string Header(string manifest, string ts, string secret = Secret) =>
        $"ts={ts},v1={Sign(manifest, secret)}";

    [Fact]
    public void Accepts_a_correctly_signed_notification()
    {
        var header = Header("id:123456;request-id:req-abc;ts:1700000000;", "1700000000");

        Assert.True(MercadoPagoSignature.IsValid(header, "req-abc", "123456", Secret));
    }

    [Fact]
    public void Omits_the_request_id_segment_when_the_header_is_absent()
    {
        var header = Header("id:123456;ts:1700000000;", "1700000000");

        Assert.True(MercadoPagoSignature.IsValid(header, null, "123456", Secret));
    }

    [Fact]
    public void Lowercases_alphanumeric_ids_in_the_manifest()
    {
        var header = Header("id:abc-def;request-id:req-1;ts:1700000000;", "1700000000");

        Assert.True(MercadoPagoSignature.IsValid(header, "req-1", "ABC-DEF", Secret));
    }

    [Fact]
    public void Rejects_a_swapped_payment_id()
    {
        // Signed for 123456, delivered claiming 999999: the exact forgery the check exists for.
        var header = Header("id:123456;request-id:req-abc;ts:1700000000;", "1700000000");

        Assert.False(MercadoPagoSignature.IsValid(header, "req-abc", "999999", Secret));
    }

    [Fact]
    public void Rejects_a_signature_made_with_another_secret()
    {
        var header = Header("id:123456;request-id:req-abc;ts:1700000000;", "1700000000", "someone-elses-secret");

        Assert.False(MercadoPagoSignature.IsValid(header, "req-abc", "123456", Secret));
    }

    [Fact]
    public void Rejects_a_replay_under_a_different_timestamp()
    {
        // v1 signed against ts=1700000000 but the header advertises a newer ts.
        var header = $"ts=1700009999,v1={Sign("id:123456;request-id:req-abc;ts:1700000000;")}";

        Assert.False(MercadoPagoSignature.IsValid(header, "req-abc", "123456", Secret));
    }

    [Theory]
    [InlineData(null)]                       // header missing entirely
    [InlineData("")]
    [InlineData("garbage")]                  // no key=value pairs
    [InlineData("ts=1700000000")]            // no v1
    [InlineData("v1=deadbeef")]              // no ts
    public void Rejects_malformed_headers(string? header)
    {
        Assert.False(MercadoPagoSignature.IsValid(header, "req-abc", "123456", Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Passes_through_when_no_secret_is_configured(string? secret)
    {
        // Integration works without a webhook secret; production must configure one.
        Assert.True(MercadoPagoSignature.IsValid(null, null, "123456", secret));
    }
}

public class MercadoPagoAmountTests
{
    [Theory]
    [InlineData("CLP", 19990.4, 19990)]   // CLP has no minor unit
    [InlineData("CLP", 19990.5, 19991)]
    [InlineData("ARS", 1234.567, 1234.57)]
    [InlineData("BRL", 99.994, 99.99)]
    public void Normalizes_to_the_currency_scale(string currency, decimal amount, decimal expected)
    {
        Assert.Equal(expected, MercadoPagoGatewayClient.NormalizeAmount(amount, currency));
    }

    [Fact]
    public void What_we_charge_is_what_we_compare_the_webhook_against()
    {
        // The charge is rounded on the way out, so the webhook must be judged on the same
        // scale — otherwise a legitimate CLP payment resolves as an amount mismatch.
        const decimal stored = 19990.4m;
        var charged = MercadoPagoGatewayClient.NormalizeAmount(stored, "CLP");

        Assert.Equal(charged, MercadoPagoGatewayClient.NormalizeAmount(charged, "CLP"));
        Assert.NotEqual(stored, charged);
    }
}
