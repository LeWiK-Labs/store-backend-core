using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments;

public sealed record ChargeInitiation(string? RedirectUrl, string? ExternalReference);

// The two URLs a gateway may need are different channels and must not be conflated:
// CallbackUrl is ours (where the gateway reaches US — Webpay's browser return, Mercado Pago's
// server-to-server notification), StorefrontResultUrl is the storefront page the BUYER lands on.
// Pointing a buyer at a machine-only endpoint is how the MP return ended up serving a 405.
public sealed record ChargeContext(string CallbackUrl, string StorefrontResultUrl);

// A refund the gateway acknowledged one way or the other. Succeeded: false is not an error —
// a gateway saying "no" is a fact worth persisting, so it travels as data, not as a failed Result.
public sealed record RefundOutcome(bool Succeeded, string? ExternalReference, string? Reason);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }

    Task<Result<ChargeInitiation>> InitiateAsync(Payment payment, string decryptedCredentialsJson,
        ChargeContext context, CancellationToken ct);

    // idempotencyKey is stable across pipeline retries of one refund request: unlike initiation
    // or confirmation, this call MOVES money, so a re-run after a concurrency conflict must not
    // hand it to the gateway twice. Gateways that support a dedupe key send it; the ones that
    // don't say so at the call site.
    Task<Result<RefundOutcome>> RefundAsync(Payment payment, decimal amount,
        string decryptedCredentialsJson, Guid idempotencyKey, CancellationToken ct);
}