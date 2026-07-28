using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments;

public sealed record ChargeInitiation(string? RedirectUrl, string? ExternalReference);

// The two URLs a gateway may need are different channels and must not be conflated:
// CallbackUrl is ours (where the gateway reaches US — Webpay's browser return, Mercado Pago's
// server-to-server notification), StorefrontResultUrl is the storefront page the BUYER lands on.
// Pointing a buyer at a machine-only endpoint is how the MP return ended up serving a 405.
public sealed record ChargeContext(string CallbackUrl, string StorefrontResultUrl);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }

    Task<Result<ChargeInitiation>> InitiateAsync(Payment payment, string decryptedCredentialsJson,
        ChargeContext context, CancellationToken ct);
}