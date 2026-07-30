using LeWiK.Store.App.Payments.Gateways;

namespace LeWiK.Tienda.Tests.Payments;

// Both gateways answer a refund with a vocabulary word, not a boolean, and reading that word
// wrong costs real money in one direction or the other: call a real refund a failure and the
// ledger says the buyer was never paid back; call a rejection a success and the store believes
// it returned money it still holds. These pin the reading down without touching the network.
public class RefundOutcomeTests
{
    // ---- Transbank ----
    // Which word you get depends on WHEN you refunded relative to the charge, not on whether
    // it worked. Same day: reversed. Later: nullified.
    [Theory]
    [InlineData("REVERSED")]
    [InlineData("NULLIFIED")]
    [InlineData("reversed")]                    // casing is not guaranteed
    [InlineData("PARTIALLY_NULLIFIED")]         // partial refund after the settlement window
    public void Transbank_reversal_and_nullification_both_mean_the_money_went_back(string type)
    {
        Assert.True(WebpayGatewayClient.IsRefundApplied(type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("REJECTED")]
    public void Transbank_anything_else_is_not_a_refund(string? type)
    {
        Assert.False(WebpayGatewayClient.IsRefundApplied(type));
    }

    // ---- Mercado Pago ----
    [Theory]
    [InlineData("rejected")]
    [InlineData("cancelled")]
    [InlineData("REJECTED")]
    public void MercadoPago_rejects_only_on_an_explicit_no(string status)
    {
        Assert.True(MercadoPagoGatewayClient.IsRefundRejected(status));
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("in_process")]      // still settling, but the buyer does get the money
    [InlineData("")]
    [InlineData(null)]
    [InlineData("some_status_we_have_not_seen")]
    public void MercadoPago_treats_everything_else_as_the_refund_having_happened(string? status)
    {
        // Deliberate asymmetry with Transbank: MP already moved the money by the time it
        // answers, so an unrecognised status must not be recorded as "no refund happened".
        Assert.False(MercadoPagoGatewayClient.IsRefundRejected(status));
    }
}
