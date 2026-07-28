using System.Text.Json;
using System.Text.Json.Serialization;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;
using MercadoPago.Client;
using MercadoPago.Client.Payment;
using MercadoPago.Client.Preference;

namespace LeWiK.Store.App.Payments.Gateways;

// Checkout Pro: create a preference, redirect the buyer to its init_point.
// Confirmation arrives asynchronously via webhook — never from the browser return.
public sealed class MercadoPagoGatewayClient : IPaymentGatewayClient
{
    public PaymentGateway Gateway => PaymentGateway.MercadoPago;

    public async Task<Result<ChargeInitiation>> InitiateAsync(
        Payment payment, string decryptedCredentialsJson, ChargeContext context, CancellationToken ct)
    {
        var creds = Parse(decryptedCredentialsJson);
        if (creds is null)
            return PaymentErrors.InvalidCredentials(PaymentGateway.MercadoPago);

        var request = BuildPreference(payment, context);

        try
        {
            var options = new RequestOptions { AccessToken = creds.AccessToken };
            // Idempotency key: retrying the same initiation won't create duplicate preferences.
            options.CustomHeaders.Add("X-Idempotency-Key", payment.Id.ToString());

            var preference = await new PreferenceClient().CreateAsync(request, options, ct);

            return new ChargeInitiation(
                RedirectUrl: preference.InitPoint,
                ExternalReference: preference.Id);
        }
        catch (Exception ex)
        {
            return PaymentErrors.GatewayFailure(PaymentGateway.MercadoPago, ex.Message);
        }
    }

    // Separated from the call so the wiring can be asserted without MP credentials: which URL
    // each channel gets is exactly what went wrong once, and it is invisible from the outside.
    internal static PreferenceRequest BuildPreference(Payment payment, ChargeContext context) => new()
    {
        Items =
        [
            new PreferenceItemRequest
            {
                Title = $"Pedido {payment.OrderId}",
                Quantity = 1,
                CurrencyId = payment.Currency,
                UnitPrice = NormalizeAmount(payment.Amount, payment.Currency),
            }
        ],
        // Our Payment id travels here and comes back when we query the payment:
        // it's how the webhook finds this record.
        ExternalReference = payment.Id.ToString(),
        // Machine channel: carries the tenant, MP calls it back server-to-server.
        NotificationUrl = context.CallbackUrl,
        // Human channel: MP sends the BUYER's browser here. It must be a page, not the
        // webhook — that endpoint only speaks POST and would greet them with a 405.
        // Purely cosmetic: the payment is settled by the notification, not by this redirect.
        BackUrls = new PreferenceBackUrlsRequest
        {
            Success = context.StorefrontResultUrl,
            Pending = context.StorefrontResultUrl,
            Failure = context.StorefrontResultUrl,
        },
    };

    // Queries a payment by MP's id (from the webhook) to learn its real status.
    // Never trust the webhook body alone — it only carries an id.
    public async Task<MpPaymentSnapshot?> FetchPaymentAsync(
        long mercadoPagoPaymentId, string decryptedCredentialsJson, CancellationToken ct)
    {
        var creds = Parse(decryptedCredentialsJson);
        if (creds is null) return null;

        try
        {
            var options = new RequestOptions { AccessToken = creds.AccessToken };
            var payment = await new PaymentClient().GetAsync(mercadoPagoPaymentId, options, ct);

            return new MpPaymentSnapshot(
                payment.Id?.ToString() ?? mercadoPagoPaymentId.ToString(),
                payment.Status ?? "unknown",
                payment.ExternalReference,
                payment.TransactionAmount ?? 0m);
        }
        catch
        {
            return null;
        }
    }

    public string? WebhookSecret(string decryptedCredentialsJson) => Parse(decryptedCredentialsJson)?.WebhookSecret;

    // Same rounding on the way out (what we ask MP to charge) and on the way back
    // (what we compare the webhook against), so a legitimate charge never looks like a mismatch.
    // CLP has no minor unit; everything else settles with cents.
    internal static decimal NormalizeAmount(decimal amount, string currency) =>
        Math.Round(amount, currency == "CLP" ? 0 : 2, MidpointRounding.AwayFromZero);

    private static MpCredentials? Parse(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<MpCredentials>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(c?.AccessToken) ? null : c;
        }
        catch (JsonException) { return null; }
    }

    private sealed record MpCredentials(
        [property: JsonPropertyName("accessToken")] string AccessToken,
        [property: JsonPropertyName("webhookSecret")] string? WebhookSecret);
}

public sealed record MpPaymentSnapshot(string PaymentId, string Status, string? ExternalReference, decimal Amount);
