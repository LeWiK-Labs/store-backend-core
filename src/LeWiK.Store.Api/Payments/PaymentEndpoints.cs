using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Payments;
using LeWiK.Store.App.Payments.Domain;
using LeWiK.Store.App.Payments.Gateways;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

        // Transbank redirects the BROWSER here — no tenant header, and depending on the flow it
        // arrives by GET (token in the query string) or POST (form fields), so we accept both and
        // read from whichever carries the data. We resolve the payment crossing tenants explicitly
        // (the only place we do this) before restoring isolation.
        //   normal flow (paid/rejected): token_ws == the create token we stored.
        //   abort / timeout: no token_ws, but TBK_ORDEN_COMPRA echoes our buyOrder (the payment id).
        app.MapMethods("/payments/webpay/return", ["GET", "POST"], async (
            HttpRequest http, TenantContext tenant, StoreDbContext db,
            IOptions<PaymentSettings> settings, ISender sender) =>
        {
            var form = http.HasFormContentType ? await http.ReadFormAsync() : null;
            string Field(string key)
            {
                var fromQuery = http.Query[key].ToString();
                return !string.IsNullOrWhiteSpace(fromQuery) ? fromQuery : form?[key].ToString() ?? "";
            }

            var tokenWs = Field("token_ws");
            var buyOrder = Field("TBK_ORDEN_COMPRA");

            var resultUrl = settings.Value.StorefrontResultUrl.TrimEnd('/');

            var payments = db.Set<Payment>().IgnoreQueryFilters();   // no tenant context yet
            Payment? payment = null;
            if (!string.IsNullOrWhiteSpace(tokenWs))
                payment = await payments.FirstOrDefaultAsync(p => p.ExternalReference == tokenWs);
            else if (WebpayGatewayClient.TryDecodeBuyOrder(buyOrder, out var paymentId))
                payment = await payments.FirstOrDefaultAsync(p => p.Id == paymentId);

            if (payment is null)
                return Results.Redirect($"{resultUrl}?status=error");

            tenant.SetTenant(payment.TenantId);              // from here on, normal isolation

            var result = await sender.Send(
                new CompleteWebpayPaymentCommand(payment.Id, Aborted: string.IsNullOrWhiteSpace(tokenWs)));

            return result.IsSuccess
                ? Results.Redirect($"{resultUrl}?orderId={result.Value.OrderId}&status={result.Value.Outcome}")
                : Results.Redirect($"{resultUrl}?status=error");
        });

        return app;
    }
}

public sealed record ConfigureMethodBody(string CredentialsJson);
public sealed record InitiateBody(PaymentGateway Gateway, PaymentType Type);
public sealed record ConfirmBody(string? ExternalReference);