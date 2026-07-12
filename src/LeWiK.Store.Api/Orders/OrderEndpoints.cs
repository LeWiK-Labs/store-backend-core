using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Orders;
using MediatR;

namespace LeWiK.Store.Api.Orders;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/orders");

        // Place an order (checkout).
        group.MapPost("/", async (CheckoutCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        group.MapGet("/{orderId:guid}", async (Guid orderId, ISender sender) =>
            (await sender.Send(new GetOrderQuery(orderId))).ToHttpResult());
        
        // Register a payment (confirm transfer / collect balance).
        group.MapPost("/{orderId:guid}/payments", async (Guid orderId, RegisterPaymentBody body, ISender sender) =>
            (await sender.Send(new RegisterPaymentCommand(orderId, body.Amount))).ToHttpResult());

// Release a preorder (stock arrived).
        group.MapPost("/{orderId:guid}/release", async (Guid orderId, ISender sender) =>
            (await sender.Send(new ReleaseOrderCommand(orderId))).ToHttpResult());

// Start preparing.
        group.MapPost("/{orderId:guid}/prepare", async (Guid orderId, ISender sender) =>
            (await sender.Send(new StartPreparingCommand(orderId))).ToHttpResult());

// Fulfill a line (full or partial pickup/shipment).
        group.MapPost("/{orderId:guid}/lines/{lineId:guid}/fulfill", async (Guid orderId, Guid lineId, FulfillLineBody body, ISender sender) =>
            (await sender.Send(new FulfillLineCommand(orderId, lineId, body.Quantity))).ToHttpResult());

// Cancel (releases reservations).
        group.MapPost("/{orderId:guid}/cancel", async (Guid orderId, ISender sender) =>
            (await sender.Send(new CancelOrderCommand(orderId))).ToHttpResult());

        return app;
    }
}

public sealed record RegisterPaymentBody(decimal Amount);
public sealed record FulfillLineBody(int Quantity);