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

    // There is no API to call: the store wires the money back itself. Recording it here is the
    // point — it keeps the ledger honest about what was returned. Nothing external happens, so
    // a retried call is harmless and the idempotency key has nothing to key.
    public Task<Result<RefundOutcome>> RefundAsync(Payment payment, decimal amount,
        string decryptedCredentialsJson, Guid idempotencyKey, CancellationToken ct)
        => Task.FromResult<Result<RefundOutcome>>(new RefundOutcome(true, "manual", null));
}