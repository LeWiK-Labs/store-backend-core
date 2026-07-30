using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Payments;

// Amount omitted = refund everything still refundable on this charge.
//
// RequestId identifies one refund *request*, and the endpoint mints it — deliberately outside
// this handler. ConcurrencyRetryBehavior re-runs the handler when SaveChanges hits a row
// version conflict (another writer touched the order: a second refund, or a payment webhook
// landing at the same moment). Every other handler is safe to re-run because its gateway call
// is a read or an already-idempotent commit. This one moves money, so the retry has to carry
// the same key into the gateway or the buyer gets refunded twice.
public sealed record RefundPaymentCommand(Guid PaymentId, decimal? Amount, string? Reason, Guid RequestId)
    : ICommand<RefundResponse>;

public sealed record RefundResponse(
    Guid RefundId, string State, decimal Amount, string Currency,
    Guid OrderId, decimal OrderPaid, decimal OrderRefunded, string? FailureReason);

public sealed class RefundPaymentValidator : AbstractValidator<RefundPaymentCommand>
{
    public RefundPaymentValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}

public sealed class RefundPaymentHandler(
    StoreDbContext db, ITenantContext tenant, CredentialProtector protector, PaymentGatewayResolver resolver)
    : IRequestHandler<RefundPaymentCommand, Result<RefundResponse>>
{
    public async Task<Result<RefundResponse>> Handle(RefundPaymentCommand request, CancellationToken ct)
    {
        var payment = await db.Set<Payment>().FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct);
        if (payment is null) return PaymentErrors.PaymentNotFound(request.PaymentId);
        if (payment.State != PaymentState.Succeeded)
            return PaymentErrors.PaymentNotRefundable(payment.Id, payment.State);

        // How much of THIS charge is still refundable. Nullable cast so an order with no refunds
        // yet reads 0 instead of tripping over SQL's NULL sum.
        var alreadyRefunded = await db.Set<Refund>()
            .Where(r => r.PaymentId == payment.Id && r.State == RefundState.Succeeded)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

        var refundable = payment.Amount - alreadyRefunded;
        var amount = request.Amount ?? refundable;
        if (amount <= 0) return PaymentErrors.RefundExceedsPayment(amount, refundable);
        if (amount > refundable) return PaymentErrors.RefundExceedsPayment(amount, refundable);

        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(payment.OrderId);

        // Check the order-level cap BEFORE the gateway call, not only inside ApplyRefund after it.
        // Both caps have to hold, and finding out afterwards would mean the money already left
        // while we return a failure that discards the record of it.
        if (amount > order.PaidAmount - order.RefundedAmount)
            return OrderErrors.RefundExceedsPaid(amount, order.PaidAmount - order.RefundedAmount);

        var config = await db.Set<PaymentMethodConfig>()
            .FirstOrDefaultAsync(c => c.Gateway == payment.Gateway, ct);

        var refund = new Refund(tenant.TenantId, payment, amount, request.Reason);

        // Manually recorded payments have no gateway to call — the store moved the money itself.
        if (payment.Gateway == PaymentGateway.Manual)
        {
            db.Add(refund);
            refund.MarkSucceeded("manual");
        }
        else
        {
            var client = resolver.For(payment.Gateway);
            if (client is null) return PaymentErrors.RefundNotSupported(payment.Gateway);
            if (config is null) return PaymentErrors.GatewayNotConfigured(payment.Gateway);

            // Added only once every reason to reject is behind us, so nothing above can leave a
            // dangling row.
            db.Add(refund);

            var credentials = protector.Unprotect(config.EncryptedCredentials);
            var outcome = await client.RefundAsync(payment, amount, credentials, request.RequestId, ct);
            if (outcome.IsFailure) return outcome.Error;

            // A refusal is not an error: the attempt happened and is worth keeping, so it is
            // persisted as Failed and returned as a successful Result with the state inside.
            if (!outcome.Value.Succeeded)
            {
                refund.MarkFailed(outcome.Value.Reason ?? "gateway rejected the refund");
                return new RefundResponse(refund.Id, refund.State.ToString(), amount, refund.Currency,
                    order.Id, order.PaidAmount, order.RefundedAmount, refund.FailureReason);
            }

            refund.MarkSucceeded(outcome.Value.ExternalReference);
        }

        // Unreachable given the pre-check above; kept so an invariant we got wrong surfaces
        // as an error rather than as a silently wrong RefundedAmount.
        var apply = order.ApplyRefund(amount);
        if (apply.IsFailure) return apply.Error;

        return new RefundResponse(refund.Id, refund.State.ToString(), amount, refund.Currency,
            order.Id, order.PaidAmount, order.RefundedAmount, null);
    }
}
