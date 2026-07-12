using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Orders;

public sealed record RegisterPaymentCommand(Guid OrderId, decimal Amount) : ICommand<OrderPaymentResponse>;

public sealed record OrderPaymentResponse(
    Guid OrderId, string PaymentStatus, string FulfillmentStatus,
    decimal Total, decimal Paid, decimal Balance);

public sealed class RegisterPaymentValidator : AbstractValidator<RegisterPaymentCommand>
{
    public RegisterPaymentValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}

public sealed class RegisterPaymentHandler(StoreDbContext db)
    : IRequestHandler<RegisterPaymentCommand, Result<OrderPaymentResponse>>
{
    public async Task<Result<OrderPaymentResponse>> Handle(RegisterPaymentCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null)
            return OrderErrors.OrderNotFound(request.OrderId);

        // The aggregate enforces the rules (can't overpay, can't pay a cancelled order, etc.)
        // and raises OrderDeposited/OrderPaid, advancing fulfillment as needed.
        var result = order.ApplyPayment(request.Amount);
        if (result.IsFailure)
            return result.Error;

        return Map(order);
    }

    internal static OrderPaymentResponse Map(Order o) => new(
        o.Id, o.PaymentStatus.ToString(), o.FulfillmentStatus.ToString(),
        o.TotalAmount, o.PaidAmount, o.BalanceAmount);
}