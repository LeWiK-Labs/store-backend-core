using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Payments;

public sealed record ConfirmPaymentCommand(Guid PaymentId, string? ExternalReference) : ICommand<PaymentConfirmationResponse>;

public sealed record PaymentConfirmationResponse(
    Guid PaymentId, string PaymentState, Guid OrderId, string OrderPaymentStatus, decimal OrderBalance);

public sealed class ConfirmPaymentHandler(StoreDbContext db)
    : IRequestHandler<ConfirmPaymentCommand, Result<PaymentConfirmationResponse>>
{
    public async Task<Result<PaymentConfirmationResponse>> Handle(ConfirmPaymentCommand request, CancellationToken ct)
    {
        var payment = await db.Set<Payment>().FirstOrDefaultAsync(p => p.Id == request.PaymentId, ct);
        if (payment is null) return PaymentErrors.PaymentNotFound(request.PaymentId);

        // Idempotency: a resolved payment doesn't apply money twice.
        var markResult = payment.MarkSucceeded(request.ExternalReference);
        if (markResult.IsFailure) return markResult.Error;

        // Include the lines: AdvanceAfterPayment (via ApplyPayment) needs them to route a
        // preorder order to AwaitingRelease instead of treating it as stock-only.
        var order = await db.Set<Order>()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == payment.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(payment.OrderId);

        // Apply the confirmed amount to the order (advances payment/fulfillment as before).
        var applyResult = order.ApplyPayment(payment.Amount);
        if (applyResult.IsFailure) return applyResult.Error;

        return new PaymentConfirmationResponse(
            payment.Id, payment.State.ToString(),
            order.Id, order.PaymentStatus.ToString(), order.BalanceAmount);
    }
}