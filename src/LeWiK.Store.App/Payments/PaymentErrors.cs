using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Payments.Domain;

namespace LeWiK.Store.App.Payments;

public static class PaymentErrors
{
    public static Error AlreadyResolved(Guid paymentId, PaymentState state) =>
        Error.Conflict("payment.already_resolved", $"Payment {paymentId} is already {state}.");
    public static Error GatewayNotConfigured(PaymentGateway gateway) =>
        Error.Conflict("payment.gateway_not_configured", $"The store has no active {gateway} configuration.");
    public static Error PaymentNotFound(Guid paymentId) =>
        Error.NotFound("payment.not_found", $"Payment {paymentId} was not found.");
}