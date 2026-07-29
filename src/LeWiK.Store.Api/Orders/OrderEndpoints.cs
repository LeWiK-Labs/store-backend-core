using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Orders;
using MediatR;

namespace LeWiK.Store.Api.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: checkout. The buyer has no account and never will — the response carries an
        // access token so they can track and pay their order afterwards.
        app.MapPost("/orders", async (CheckoutCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        // Admin: everything about an order that already exists. Reading one by bare id used to
        // be public, which meant a guessed or leaked GUID handed over a stranger's name, email
        // and phone. Guests now go through /pay/{token} instead.
        var admin = app.MapGroup("/admin/orders")
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        admin.MapGet("/{orderId:guid}", async (Guid orderId, ISender sender) =>
            (await sender.Send(new GetOrderQuery(orderId))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/payments", async (Guid orderId, RegisterPaymentBody body, ISender sender) =>
            (await sender.Send(new RegisterPaymentCommand(orderId, body.Amount))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/release", async (Guid orderId, ISender sender) =>
            (await sender.Send(new ReleaseOrderCommand(orderId))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/prepare", async (Guid orderId, ISender sender) =>
            (await sender.Send(new StartPreparingCommand(orderId))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/lines/{lineId:guid}/fulfill",
            async (Guid orderId, Guid lineId, FulfillLineBody body, ISender sender) =>
                (await sender.Send(new FulfillLineCommand(orderId, lineId, body.Quantity))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/cancel", async (Guid orderId, ISender sender) =>
            (await sender.Send(new CancelOrderCommand(orderId))).ToHttpResult());

        admin.MapPost("/{orderId:guid}/payment-link", async (Guid orderId, PaymentLinkBody? body, ISender sender) =>
            (await sender.Send(new CreatePaymentLinkCommand(orderId, body?.ValidForDays))).ToHttpResult());

        return app;
    }
}

public sealed record RegisterPaymentBody(decimal Amount);
public sealed record FulfillLineBody(int Quantity);
public sealed record PaymentLinkBody(int? ValidForDays);
