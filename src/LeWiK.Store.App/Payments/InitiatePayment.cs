using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;
using LeWiK.Store.App.Orders;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.App.Payments;

public sealed record InitiatePaymentCommand(Guid OrderId, PaymentGateway Gateway, PaymentType Type)
    : ICommand<PaymentInitiationResponse>;

public sealed record PaymentInitiationResponse(
    Guid PaymentId, string Gateway, decimal Amount, string Currency,
    string? RedirectUrl, string? RedirectToken, string? BankDetails);

public sealed class InitiatePaymentValidator : AbstractValidator<InitiatePaymentCommand>
{
    public InitiatePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class InitiatePaymentHandler(
    StoreDbContext db, ITenantContext tenant, CredentialProtector protector,
    PaymentGatewayResolver resolver, IOptions<PaymentSettings> settings,
    IOptions<ReservationSettings> reservations)
    : IRequestHandler<InitiatePaymentCommand, Result<PaymentInitiationResponse>>
{
    public async Task<Result<PaymentInitiationResponse>> Handle(InitiatePaymentCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

        // Refuse to send a buyer to a gateway for an order that no longer exists commercially.
        // ApplyPayment already rejects a cancelled order, but by then the card has been charged
        // and the money needs a manual refund. Now that reservations expire on their own, this
        // stopped being a rare admin-cancelled edge case: the sweeper cancels orders every minute.
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            return OrderErrors.OrderCancelled();

        // The amount to charge depends on the type: full/balance = remaining balance,
        // deposit = the required deposit not yet covered.
        var amount = request.Type == PaymentType.Deposit
            ? order.DepositDueAmount - order.PaidAmount
            : order.BalanceAmount;
        if (amount <= 0) return OrderErrors.AlreadyPaid();

        var config = await db.Set<PaymentMethodConfig>()
            .FirstOrDefaultAsync(c => c.Gateway == request.Gateway && c.IsActive, ct);
        if (config is null) return PaymentErrors.GatewayNotConfigured(request.Gateway);

        var client = resolver.For(request.Gateway);
        if (client is null) return PaymentErrors.GatewayNotConfigured(request.Gateway);

        var credentials = protector.Unprotect(config.EncryptedCredentials);

        var payment = new Payment(tenant.TenantId, order.Id, request.Gateway, request.Type, amount, order.Currency);
        db.Add(payment);

        // The buyer is about to enter their card: push the deadline out so the sweeper can't
        // cancel the order and release its stock while they are still on the gateway's page —
        // which would take the money and leave nothing to apply it to.
        if (reservations.Value.Enabled)
            order.ExtendReservation(DateTime.UtcNow.AddMinutes(reservations.Value.PaymentGraceMinutes));

        var baseUrl = settings.Value.ReturnUrlBase.TrimEnd('/');
        var callbackUrl = request.Gateway switch
        {
            // MP calls this back server-to-server with no headers: the tenant rides in the path.
            PaymentGateway.MercadoPago => $"{baseUrl}/payments/mercadopago/webhook/{tenant.TenantId}",
            PaymentGateway.Webpay => $"{baseUrl}/payments/webpay/return",
            _ => "",
        };
        var context = new ChargeContext(callbackUrl, settings.Value.StorefrontResultUrl);

        var initiation = await client.InitiateAsync(payment, credentials, context, ct);
        if (initiation.IsFailure) return initiation.Error;

        // Gateways return their own reference (Webpay's token); it's how the return
        // endpoint finds this payment later.
        if (!string.IsNullOrWhiteSpace(initiation.Value.ExternalReference))
            payment.SetExternalReference(initiation.Value.ExternalReference);

        // For transfer, expose the store's bank details (the decrypted config) to the buyer.
        var bankDetails = request.Gateway == PaymentGateway.Transfer ? credentials : null;

        return new PaymentInitiationResponse(
            payment.Id, payment.Gateway.ToString(), amount, order.Currency,
            initiation.Value.RedirectUrl, initiation.Value.ExternalReference, bankDetails);
    }
}
