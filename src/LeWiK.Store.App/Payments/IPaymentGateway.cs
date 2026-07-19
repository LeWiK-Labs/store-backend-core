using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments;

public sealed record ChargeInitiation(string? RedirectUrl, string? ExternalReference);

public interface IPaymentGatewayClient
{
    PaymentGateway Gateway { get; }

    Task<Result<ChargeInitiation>> InitiateAsync(Payment payment, string decryptedCredentialsJson, string returnUrl,
        CancellationToken ct);
}