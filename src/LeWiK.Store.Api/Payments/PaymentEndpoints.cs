using System.Text.Json;
using LeWiK.Store.Api.Auth;
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
        // OWNER ONLY. Gateway credentials decide which bank account the store's money lands in,
        // so whoever can change them can redirect every future payment. It is the single most
        // dangerous privilege in the system and the only thing restricted this tightly.
        app.MapPut("/admin/payment-methods/{gateway}",
                async (PaymentGateway gateway, ConfigureMethodBody body, ISender sender) =>
                    (await sender.Send(new ConfigurePaymentMethodCommand(gateway, body.CredentialsJson))).ToHttpResult())
            .RequireAuthorization(AuthPolicies.StoreOwner)
            .RequireCsrfHeader();

        // Public: the buyer pays immediately after checkout, before any account exists.
        app.MapPost("/orders/{orderId:guid}/payments/initiate", async (Guid orderId, InitiateBody body, ISender sender) =>
            (await sender.Send(new InitiatePaymentCommand(orderId, body.Gateway, body.Type))).ToHttpResult());

        // Admin: confirming money received, refunding, auditing the charges of an order.
        var admin = app.MapGroup("/admin")
            .RequireAuthorization(AuthPolicies.StoreStaff)
            .RequireCsrfHeader();

        // Store: confirm a payment (transfer received). Gateways call the equivalent internally.
        admin.MapPost("/payments/{paymentId:guid}/confirm", async (Guid paymentId, ConfirmBody? body, ISender sender) =>
            (await sender.Send(new ConfirmPaymentCommand(paymentId, body?.ExternalReference))).ToHttpResult());

        // Store: the charges of an order, with how much of each has been refunded.
        admin.MapGet("/orders/{orderId:guid}/payments", async (Guid orderId, ISender sender) =>
            (await sender.Send(new ListOrderPaymentsQuery(orderId))).ToHttpResult());

        // Store: refund a specific charge (full when no amount is given, partial otherwise).
        admin.MapPost("/payments/{paymentId:guid}/refund", async (
            Guid paymentId, RefundBody? body, HttpRequest http, ISender sender) =>
        {
            // One key per refund REQUEST, minted here so it survives the pipeline's concurrency
            // retries — the handler re-runs, the key does not change, and the gateway sees the
            // replay for what it is. An Idempotency-Key header extends the same protection to a
            // client that retries the HTTP call itself.
            var key = Guid.TryParse(http.Headers["Idempotency-Key"].FirstOrDefault(), out var supplied)
                ? supplied
                : Guid.CreateVersion7();

            return (await sender.Send(
                new RefundPaymentCommand(paymentId, body?.Amount, body?.Reason, key))).ToHttpResult();
        });

        // Gateway callbacks stay public below: Transbank and Mercado Pago are the callers, and
        // neither can present a session. They authenticate by other means — Webpay by the token
        // it hands back, MP by the x-signature on its notification.

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
            // Gate on the content type: ReadFromJsonAsync throws InvalidOperationException on a
            // bodyless POST, and an exception here becomes a 500 — which is what puts MP into
            // the retry loop this endpoint exists to avoid.
            if (string.IsNullOrWhiteSpace(raw) && http.HasJsonContentType())
            {
                try
                {
                    var body = await http.ReadFromJsonAsync<MercadoPagoNotification>();
                    raw = body?.Data?.Id;
                    topic ??= body?.Type ?? body?.Topic;
                }
                catch (Exception ex) when (ex is JsonException or BadHttpRequestException) { /* malformed */ }
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
public sealed record RefundBody(decimal? Amount, string? Reason);