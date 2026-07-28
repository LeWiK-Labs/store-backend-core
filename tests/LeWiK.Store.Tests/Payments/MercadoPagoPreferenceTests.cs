using LeWiK.Store.App.Payments;
using LeWiK.Store.App.Payments.Domain;
using LeWiK.Store.App.Payments.Gateways;

namespace LeWiK.Tienda.Tests.Payments;

// The preference is the whole contract with Mercado Pago, and every field in it is invisible
// until a real buyer hits it: which URL each channel gets, what links the webhook back to us,
// what we actually ask to be charged.
public class MercadoPagoPreferenceTests
{
    private const string Webhook = "https://api.tienda.cl/payments/mercadopago/webhook/11111111-1111-1111-1111-111111111111";
    private const string Storefront = "https://tienda.cl/pago/resultado";

    private static (Payment payment, ChargeContext context) Sample(decimal amount = 33000m, string currency = "CLP")
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), PaymentGateway.MercadoPago,
            PaymentType.Full, amount, currency);
        return (payment, new ChargeContext(Webhook, Storefront));
    }

    [Fact]
    public void Buyer_is_sent_to_the_storefront_never_to_the_webhook()
    {
        // The webhook only speaks POST: pointing back_urls at it serves the buyer a 405.
        var (payment, context) = Sample();

        var request = MercadoPagoGatewayClient.BuildPreference(payment, context);

        Assert.Equal(Storefront, request.BackUrls.Success);
        Assert.Equal(Storefront, request.BackUrls.Pending);
        Assert.Equal(Storefront, request.BackUrls.Failure);
        Assert.NotEqual(request.NotificationUrl, request.BackUrls.Success);
    }

    [Fact]
    public void Notification_url_is_the_tenant_scoped_webhook()
    {
        // The tenant rides in the path because the server-to-server call carries no headers.
        var (payment, context) = Sample();

        var request = MercadoPagoGatewayClient.BuildPreference(payment, context);

        Assert.Equal(Webhook, request.NotificationUrl);
        Assert.Contains("11111111-1111-1111-1111-111111111111", request.NotificationUrl);
    }

    [Fact]
    public void External_reference_carries_our_payment_id()
    {
        // This is the only bridge from MP's payment id back to our record: the preference id
        // we store at initiation is a different number than the one the webhook announces.
        var (payment, context) = Sample();

        var request = MercadoPagoGatewayClient.BuildPreference(payment, context);

        Assert.Equal(payment.Id.ToString(), request.ExternalReference);
        Assert.True(Guid.TryParse(request.ExternalReference, out var parsed));
        Assert.Equal(payment.Id, parsed);
    }

    [Theory]
    [InlineData("CLP", 19990.4, 19990)]     // no minor unit
    [InlineData("CLP", 33000, 33000)]
    [InlineData("ARS", 1234.567, 1234.57)]
    public void Charged_amount_matches_the_currency_scale(string currency, decimal amount, decimal expected)
    {
        var (payment, context) = Sample(amount, currency);

        var request = MercadoPagoGatewayClient.BuildPreference(payment, context);

        Assert.Equal(expected, request.Items[0].UnitPrice);
        Assert.Equal(currency, request.Items[0].CurrencyId);
        Assert.Equal(1, request.Items[0].Quantity);
    }

    [Fact]
    public void Charged_amount_is_what_the_webhook_will_be_compared_against()
    {
        // Round-trip guard: initiation and confirmation must agree, or a legitimate CLP
        // payment resolves as an amount mismatch and gets marked failed.
        var (payment, context) = Sample(19990.4m);

        var request = MercadoPagoGatewayClient.BuildPreference(payment, context);
        var charged = request.Items[0].UnitPrice!.Value;

        Assert.Equal(charged, MercadoPagoGatewayClient.NormalizeAmount(charged, payment.Currency));
    }
}
