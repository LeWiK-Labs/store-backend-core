using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments.Gateways;

public sealed class TransferGatewayClient : IPaymentGatewayClient
{
    public PaymentGateway Gateway => PaymentGateway.Transfer;

    public Task<Result<ChargeInitiation>> InitiateAsync(Payment payment, string decryptedCredentialsJson, ChargeContext context, CancellationToken ct)
    {
        return Task.FromResult<Result<ChargeInitiation>>(new ChargeInitiation(RedirectUrl: null,
            ExternalReference: null));
    }
}