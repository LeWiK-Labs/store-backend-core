using System.Text.Json;
using System.Text.Json.Serialization;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;
using Transbank.Webpay.WebpayPlus;

namespace LeWiK.Store.App.Payments.Gateways;

// Webpay Plus redirect flow. Create returns a token + a form URL the buyer must be
// POSTed to; Commit confirms the charge when Transbank redirects them back.
public sealed class WebpayGatewayClient : IPaymentGatewayClient
{
    public PaymentGateway Gateway => PaymentGateway.Webpay;

    public Task<Result<ChargeInitiation>> InitiateAsync(
        Payment payment, string decryptedCredentialsJson, ChargeContext context, CancellationToken ct)
    {
        var creds = Parse(decryptedCredentialsJson);
        if (creds is null)
            return Task.FromResult<Result<ChargeInitiation>>(PaymentErrors.InvalidCredentials(PaymentGateway.Webpay));

        // Webpay settles in CLP only.
        if (payment.Currency != "CLP")
            return Task.FromResult<Result<ChargeInitiation>>(
                PaymentErrors.UnsupportedCurrency(PaymentGateway.Webpay, payment.Currency));

        try
        {
            var transaction = Build(creds);
            var response = transaction.Create(
                buyOrder: ShortId(payment.Id),      // Webpay caps buyOrder at 26 chars
                sessionId: ShortId(payment.OrderId),
                amount: (int)Math.Round(payment.Amount, MidpointRounding.AwayFromZero), // CLP has no cents
                // Transbank redirects the buyer's BROWSER here; our return endpoint takes it
                // from there and forwards them to the storefront result page.
                returnUrl: context.CallbackUrl);

            return Task.FromResult<Result<ChargeInitiation>>(
                new ChargeInitiation(RedirectUrl: response.Url, ExternalReference: response.Token));
        }
        catch (Exception ex)
        {
            return Task.FromResult<Result<ChargeInitiation>>(
                PaymentErrors.GatewayFailure(PaymentGateway.Webpay, ex.Message));
        }
    }

    // Refunds go against the ORIGINAL charge's token, which is why refunds target a Payment
    // rather than an order.
    //
    // Transbank's API has no idempotency header, so idempotencyKey cannot be honoured here: a
    // duplicate call is a second, independent refund. Transbank rejects one that exceeds the
    // remaining balance, so replaying a full refund fails safely — replaying a PARTIAL one
    // would go through twice. The command carries the key so the caller only builds one refund
    // request per user action; that is what keeps this call from being repeated.
    public Task<Result<RefundOutcome>> RefundAsync(Payment payment, decimal amount,
        string decryptedCredentialsJson, Guid idempotencyKey, CancellationToken ct)
    {
        var creds = Parse(decryptedCredentialsJson);
        if (creds is null)
            return Task.FromResult<Result<RefundOutcome>>(PaymentErrors.InvalidCredentials(PaymentGateway.Webpay));
        if (string.IsNullOrWhiteSpace(payment.ExternalReference))
            return Task.FromResult<Result<RefundOutcome>>(PaymentErrors.TokenNotFound());

        try
        {
            // Verified against TransbankSDK 7.2.0: Refund(string token, decimal amount).
            // CLP has no cents, and the token is the one Create returned at initiation.
            var response = Build(creds).Refund(
                payment.ExternalReference,
                Math.Round(amount, MidpointRounding.AwayFromZero));

            var type = response?.Type ?? "";
            return Task.FromResult<Result<RefundOutcome>>(
                IsRefundApplied(type)
                    ? new RefundOutcome(true, type, null)
                    : new RefundOutcome(false, null, $"type={type}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult<Result<RefundOutcome>>(new RefundOutcome(false, null, ex.Message));
        }
    }

    // Transbank answers with a TYPE, not a boolean, and which one you get depends on WHEN you
    // asked: refunding on the same day as the charge REVERSED it, later it NULLIFIED it (and a
    // partial one comes back as a mall/partial nullification). All of them mean the buyer has
    // the money back. Reading only "NULLIFIED" would report a same-day refund as a failure.
    // Separated from the call so this reading can be pinned down without hitting Transbank.
    internal static bool IsRefundApplied(string? type) =>
        !string.IsNullOrWhiteSpace(type)
        && (type.Contains("REVERS", StringComparison.OrdinalIgnoreCase)
         || type.Contains("NULLIF", StringComparison.OrdinalIgnoreCase));

    // Called by the return endpoint to confirm the charge.
    public CommitOutcome Commit(string token, string decryptedCredentialsJson)
    {
        var creds = Parse(decryptedCredentialsJson);
        if (creds is null) return CommitOutcome.Failed("invalid credentials");

        try
        {
            var response = Build(creds).Commit(token);
            // ResponseCode 0 + AUTHORIZED is the only success combination.
            var approved = response.ResponseCode == 0
                && string.Equals(response.Status, "AUTHORIZED", StringComparison.OrdinalIgnoreCase);

            return approved
                ? CommitOutcome.Approved(response.AuthorizationCode, response.Amount)
                : CommitOutcome.Failed($"code={response.ResponseCode} status={response.Status}");
        }
        catch (Exception ex)
        {
            return CommitOutcome.Failed(ex.Message);
        }
    }

    // Static factories exist in the .NET SDK (verified against TransbankSDK 7.2.0):
    // Transaction.buildForIntegration / buildForProduction.
    private static Transaction Build(WebpayCredentials c) =>
        c.IsProduction
            ? Transaction.buildForProduction(c.CommerceCode, c.ApiKey)
            : Transaction.buildForIntegration(c.CommerceCode, c.ApiKey);

    private static WebpayCredentials? Parse(string json)
    {
        try
        {
            var c = JsonSerializer.Deserialize<WebpayCredentials>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return string.IsNullOrWhiteSpace(c?.CommerceCode) || string.IsNullOrWhiteSpace(c.ApiKey) ? null : c;
        }
        catch (JsonException) { return null; }
    }

    // 16-byte GUID as url-safe base64 = 22 chars, under Webpay's 26-char limit.
    internal static string ShortId(Guid id) =>
        Convert.ToBase64String(id.ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Reverse of ShortId. When the buyer aborts or the form times out there is no token_ws,
    // but Transbank echoes our buyOrder back as TBK_ORDEN_COMPRA — so we recover the payment
    // id straight from it, independent of TBK_TOKEN (which differs from the create token).
    public static bool TryDecodeBuyOrder(string? buyOrder, out Guid id)
    {
        id = Guid.Empty;
        if (string.IsNullOrWhiteSpace(buyOrder)) return false;
        var b64 = buyOrder.Replace('-', '+').Replace('_', '/');
        b64 += (b64.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        try
        {
            var bytes = Convert.FromBase64String(b64);
            if (bytes.Length != 16) return false;
            id = new Guid(bytes);
            return true;
        }
        catch (FormatException) { return false; }
    }

    private sealed record WebpayCredentials(
        [property: JsonPropertyName("commerceCode")] string CommerceCode,
        [property: JsonPropertyName("apiKey")] string ApiKey,
        [property: JsonPropertyName("production")] bool IsProduction = false);
}

public sealed record CommitOutcome(bool IsApproved, string? AuthorizationCode, decimal? Amount, string? Reason)
{
    public static CommitOutcome Approved(string? code, decimal? amount) => new(true, code, amount, null);
    public static CommitOutcome Failed(string reason) => new(false, null, null, reason);
}
