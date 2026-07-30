using FluentValidation;
using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Orders.Domain;
using LeWiK.Store.App.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.App.Orders;

public sealed record CreatePaymentLinkCommand(Guid OrderId, int? ValidForDays) : ICommand<PaymentLinkResponse>;

public sealed record PaymentLinkResponse(
    Guid OrderId, string Url, DateTime ExpiresAt, decimal Balance, string Currency);

public sealed class CreatePaymentLinkValidator : AbstractValidator<CreatePaymentLinkCommand>
{
    public CreatePaymentLinkValidator()
    {
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.ValidForDays).InclusiveBetween(1, 365).When(x => x.ValidForDays.HasValue);
    }
}

public sealed class CreatePaymentLinkHandler(StoreDbContext db, IOptions<PaymentSettings> settings)
    : IRequestHandler<CreatePaymentLinkCommand, Result<PaymentLinkResponse>>
{
    public async Task<Result<PaymentLinkResponse>> Handle(CreatePaymentLinkCommand request, CancellationToken ct)
    {
        var order = await db.Set<Order>().FirstOrDefaultAsync(o => o.Id == request.OrderId, ct);
        if (order is null) return OrderErrors.OrderNotFound(request.OrderId);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled) return OrderErrors.OrderCancelled();
        if (order.BalanceAmount <= 0) return OrderErrors.NothingToPay();

        var token = PaymentLinkTokens.Generate();
        var expiresAt = DateTime.UtcNow.AddDays(request.ValidForDays ?? 30);
        order.SetPaymentLink(PaymentLinkTokens.Hash(token), expiresAt);

        // The raw token exists only in this response. It is never stored, so it cannot be shown
        // again — losing the link means issuing a new one, which retires this one on the spot.
        var url = $"{settings.Value.StorefrontPayUrlBase.TrimEnd('/')}/{token}";

        return new PaymentLinkResponse(order.Id, url, expiresAt, order.BalanceAmount, order.Currency);
    }
}
