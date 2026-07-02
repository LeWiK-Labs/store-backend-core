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

        return app;
    }
}