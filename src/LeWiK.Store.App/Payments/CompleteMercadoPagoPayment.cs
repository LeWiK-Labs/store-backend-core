using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments.Domain;
using LeWiK.Store.App.Payments.Gateways;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Payments;

// The signature fields come straight off the notification's headers/query. They are plain
// strings, so verifying here (where the store's credentials are already loaded) costs one
// config read instead of two and keeps the endpoint thin.
public sealed record CompleteMercadoPagoPaymentCommand(
    long MercadoPagoPaymentId, string RawDataId, string? Signature, string? RequestId)
    : ICommand<WebhookResultResponse>;

public sealed record WebhookResultResponse(Guid? PaymentId, string Outcome);

public sealed class CompleteMercadoPagoPaymentHandler(StoreDbContext db, CredentialProtector protector)
    : IRequestHandler<CompleteMercadoPagoPaymentCommand, Result<WebhookResultResponse>>
{
    public async Task<Result<WebhookResultResponse>> Handle(CompleteMercadoPagoPaymentCommand request, CancellationToken ct)
    {
        var config = await db.Set<PaymentMethodConfig>()
            .FirstOrDefaultAsync(c => c.Gateway == PaymentGateway.MercadoPago && c.IsActive, ct);
        if (config is null) return PaymentErrors.GatewayNotConfigured(PaymentGateway.MercadoPago);

        var credentials = protector.Unprotect(config.EncryptedCredentials);
        var client = new MercadoPagoGatewayClient();

        // Authenticate the caller before spending an API call on them: without this, anyone
        // who knows the URL could make us chase arbitrary payment ids. No secret configured
        // (integration) means the check passes through.
        if (!MercadoPagoSignature.IsValid(
                request.Signature, request.RequestId, request.RawDataId, client.WebhookSecret(credentials)))
            return PaymentErrors.InvalidWebhookSignature(PaymentGateway.MercadoPago);

        // The notification only carries an id; ask MP what actually happened.
        var snapshot = await client.FetchPaymentAsync(request.MercadoPagoPaymentId, credentials, ct);
        if (snapshot is null)
            return PaymentErrors.GatewayFailure(PaymentGateway.MercadoPago, "could not fetch payment");

        // external_reference is our own Payment id (set when the preference was created).
        if (!Guid.TryParse(snapshot.ExternalReference, out var paymentId))
            return PaymentErrors.TokenNotFound();

        var payment = await db.Set<Payment>().FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null) return PaymentErrors.PaymentNotFound(paymentId);

        // Idempotency: MP retries notifications, so duplicates are normal, not errors.
        if (payment.State != PaymentState.Pending)
            return new WebhookResultResponse(payment.Id, "already_resolved");

        // Anything other than approved is either still moving or final-negative.
        if (!string.Equals(snapshot.Status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            if (snapshot.Status is "pending" or "in_process")
                return new WebhookResultResponse(payment.Id, "pending"); // wait for the next notification

            payment.MarkFailed($"mercadopago status: {snapshot.Status}");
            return new WebhookResultResponse(payment.Id, "rejected");
        }

        // Compare on the same scale we charged on, or a legitimate CLP charge reads as a mismatch.
        var charged = MercadoPagoGatewayClient.NormalizeAmount(snapshot.Amount, payment.Currency);
        var expected = MercadoPagoGatewayClient.NormalizeAmount(payment.Amount, payment.Currency);
        if (charged != expected)
        {
            payment.MarkFailed($"amount mismatch: charged {charged}, expected {expected}");
            return new WebhookResultResponse(payment.Id, "rejected");
        }

        payment.MarkSucceeded(snapshot.PaymentId); // store MP's payment id for reconciliation

        // Include the lines: ApplyPayment routes a preorder order to AwaitingRelease
        // instead of treating it as stock-only.
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(payment.OrderId);

        var apply = order.ApplyPayment(payment.Amount);
        if (apply.IsFailure) return apply.Error;

        return new WebhookResultResponse(payment.Id, "approved");
    }
}
