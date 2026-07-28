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

public sealed record CompleteWebpayPaymentCommand(Guid PaymentId, bool Aborted) : ICommand<WebpayResultResponse>;

public sealed record WebpayResultResponse(Guid OrderId, string Outcome, string? Reason);

public sealed class CompleteWebpayPaymentHandler(StoreDbContext db, CredentialProtector protector)
    : IRequestHandler<CompleteWebpayPaymentCommand, Result<WebpayResultResponse>>
{
    public async Task<Result<WebpayResultResponse>> Handle(CompleteWebpayPaymentCommand request, CancellationToken ct)
    {
        var payment = await db.Set<Payment>()
            .FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct);
        if (payment is null) return PaymentErrors.PaymentNotFound(request.PaymentId);

        // Idempotency first: a resolved payment reports its outcome and is never re-processed.
        if (payment.State != PaymentState.Pending)
            return new WebpayResultResponse(payment.OrderId,
                payment.State == PaymentState.Succeeded ? "approved" : "rejected", "already resolved");

        // Buyer aborted or the form timed out at Transbank: close the attempt, order untouched.
        if (request.Aborted)
        {
            payment.MarkFailed("aborted by buyer");
            return new WebpayResultResponse(payment.OrderId, "aborted", null);
        }

        var config = await db.Set<PaymentMethodConfig>()
            .FirstOrDefaultAsync(c => c.Gateway == PaymentGateway.Webpay && c.IsActive, ct);
        if (config is null) return PaymentErrors.GatewayNotConfigured(PaymentGateway.Webpay);

        // The commit token is the create token we stored as ExternalReference on initiation
        // (token_ws echoes it back on the normal flow).
        if (string.IsNullOrWhiteSpace(payment.ExternalReference)) return PaymentErrors.TokenNotFound();

        var outcome = new WebpayGatewayClient()
            .Commit(payment.ExternalReference, protector.Unprotect(config.EncryptedCredentials));

        if (!outcome.IsApproved)
        {
            payment.MarkFailed(outcome.Reason ?? "rejected");
            return new WebpayResultResponse(payment.OrderId, "rejected", outcome.Reason);
        }

        // Sanity check: never apply more than what we asked Transbank to charge.
        if (outcome.Amount is not null && outcome.Amount != Math.Round(payment.Amount))
        {
            payment.MarkFailed($"amount mismatch: charged {outcome.Amount}, expected {payment.Amount}");
            return new WebpayResultResponse(payment.OrderId, "rejected", "amount mismatch");
        }

        payment.MarkSucceeded(outcome.AuthorizationCode);

        // Include the lines: ApplyPayment routes a preorder order to AwaitingRelease
        // instead of treating it as stock-only.
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(payment.OrderId);

        var apply = order.ApplyPayment(payment.Amount);
        if (apply.IsFailure) return apply.Error;

        return new WebpayResultResponse(order.Id, "approved", null);
    }
}
