using System.Text.Json;
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

        // MP posts here server-to-server. Tenant comes from the path (registered as the
        // preference's notification_url). Always answer 200 fast: a non-200 makes MP retry.
        app.MapPost("/payments/mercadopago/webhook/{tenantId:guid}", async (
            Guid tenantId, HttpRequest http, TenantContext tenant, ISender sender,
            ILogger<Program> logger) =>
        {
            tenant.SetTenant(tenantId);

            // MP sends the payment id as ?data.id= (or ?id= on older formats).
            var raw = http.Query["data.id"].FirstOrDefault() ?? http.Query["id"].FirstOrDefault();
            var topic = http.Query["type"].FirstOrDefault() ?? http.Query["topic"].FirstOrDefault();

            // Same notification also arrives as a JSON body; fall back to it so a delivery
            // without query params confirms the payment instead of silently acking nothing.
            if (string.IsNullOrWhiteSpace(raw))
            {
                try
                {
                    var body = await http.ReadFromJsonAsync<MercadoPagoNotification>();
                    raw = body?.Data?.Id;
                    topic ??= body?.Type ?? body?.Topic;
                }
                catch (Exception ex) when (ex is JsonException or BadHttpRequestException) { /* not JSON */ }
            }

            // We only care about payment notifications; ack everything else. An absent topic is
            // not a filter — merchant_order and the like always name themselves.
            if (!string.IsNullOrWhiteSpace(topic) && !topic.Contains("payment", StringComparison.OrdinalIgnoreCase))
                return Results.Ok();

            if (!long.TryParse(raw, out var mpPaymentId))
                return Results.Ok(); // nothing actionable; don't make MP retry forever

            var result = await sender.Send(new CompleteMercadoPagoPaymentCommand(
                mpPaymentId,
                RawDataId: raw!,
                Signature: http.Headers["x-signature"].FirstOrDefault(),
                RequestId: http.Headers["x-request-id"].FirstOrDefault()));

            if (result.IsFailure)
                logger.LogWarning("MercadoPago webhook failed: {Code}", result.Error.Code);
            else
                logger.LogInformation("MercadoPago webhook {Outcome} for payment {PaymentId}",
                    result.Value.Outcome, result.Value.PaymentId);

            // Ack regardless: retries won't fix a bad payload, and duplicates are safe.
            return Results.Ok();
        });

        return app;
    }
}

// Body shape of MP's notification: {"type":"payment","data":{"id":"123"}}
public sealed record MercadoPagoNotification(string? Type, string? Topic, MercadoPagoNotificationData? Data);
public sealed record MercadoPagoNotificationData(string? Id);

public sealed record ConfigureMethodBody(string CredentialsJson);
public sealed record InitiateBody(PaymentGateway Gateway, PaymentType Type);
public sealed record ConfirmBody(string? ExternalReference);