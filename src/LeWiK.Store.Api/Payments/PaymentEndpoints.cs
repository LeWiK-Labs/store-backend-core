using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Payments;
using LeWiK.Store.App.Payments.Domain;
using MediatR;

namespace LeWiK.Store.Api.Payments;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin: configure a gateway's credentials (BYOC).
        app.MapPut("/payment-methods/{gateway}", async (PaymentGateway gateway, ConfigureMethodBody body, ISender sender) =>
            (await sender.Send(new ConfigurePaymentMethodCommand(gateway, body.CredentialsJson))).ToHttpResult());

        // Buyer: start paying an order with a gateway.
        app.MapPost("/orders/{orderId:guid}/payments/initiate", async (Guid orderId, InitiateBody body, ISender sender) =>
            (await sender.Send(new InitiatePaymentCommand(orderId, body.Gateway, body.Type))).ToHttpResult());

        // Store: confirm a payment (transfer received). Gateways will call the equivalent internally.
        app.MapPost("/payments/{paymentId:guid}/confirm", async (Guid paymentId, ConfirmBody? body, ISender sender) =>
            (await sender.Send(new ConfirmPaymentCommand(paymentId, body?.ExternalReference))).ToHttpResult());

        return app;
    }
}

public sealed record ConfigureMethodBody(string CredentialsJson);
public sealed record InitiateBody(PaymentGateway Gateway, PaymentType Type);
public sealed record ConfirmBody(string? ExternalReference);