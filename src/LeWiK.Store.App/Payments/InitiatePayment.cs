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

namespace LeWiK.Store.App.Payments;

public sealed record InitiatePaymentCommand(Guid OrderId, PaymentGateway Gateway, PaymentType Type)
    : ICommand<PaymentInitiationResponse>;

public sealed record PaymentInitiationResponse(Guid PaymentId, string Gateway, decimal Amount, string Currency, string? RedirectUrl, string? BankDetails);

public sealed class InitiatePaymentValidator : AbstractValidator<InitiatePaymentCommand>
{
    public InitiatePaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
    }
}

public sealed class InitiatePaymentHandler(
    StoreDbContext db, ITenantContext tenant, CredentialProtector protector, PaymentGatewayResolver resolver)
    : IRequestHandler<InitiatePaymentCommand, Result<PaymentInitiationResponse>>
{
    public async Task<Result<PaymentInitiationResponse>> Handle(InitiatePaymentCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);

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

        var initiation = await client.InitiateAsync(payment, credentials, returnUrl: "", ct);
        if (initiation.IsFailure) return initiation.Error;

        // For transfer, expose the store's bank details (the decrypted config) to the buyer.
        var bankDetails = request.Gateway == PaymentGateway.Transfer ? credentials : null;

        return new PaymentInitiationResponse(
            payment.Id, payment.Gateway.ToString(), amount, order.Currency,
            initiation.Value.RedirectUrl, bankDetails);
    }
}
