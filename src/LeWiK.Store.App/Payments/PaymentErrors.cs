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
    public static Error InvalidCredentials(PaymentGateway gateway) =>
        Error.Conflict("payment.invalid_credentials", $"The stored {gateway} credentials are incomplete or malformed.");
    public static Error UnsupportedCurrency(PaymentGateway gateway, string currency) =>
        Error.Conflict("payment.unsupported_currency", $"{gateway} does not support {currency}.");
    public static Error GatewayFailure(PaymentGateway gateway, string reason) =>
        Error.Conflict("payment.gateway_failure", $"{gateway} rejected the request: {reason}");
    public static Error TokenNotFound() =>
        Error.NotFound("payment.token_not_found", "No pending payment matches that gateway token.");
}