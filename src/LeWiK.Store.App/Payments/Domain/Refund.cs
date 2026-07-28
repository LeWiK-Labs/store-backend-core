using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Payments.Domain;

public enum RefundState { Pending, Succeeded, Failed }

// A refund against a specific successful Payment, not against the order: giving money back
// needs the original charge's gateway reference (Webpay's token, MP's payment id) and that
// gateway's credentials, and both live on the Payment. An order paid with a deposit plus a
// balance has two charges and is refunded twice, separately.
//
// It resolves exactly once, mirroring Payment: that single-resolution rule is what makes
// duplicate calls and pipeline retries safe.
public sealed class Refund : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid PaymentId { get; private init; }
    public Guid OrderId { get; private init; }
    public decimal Amount { get; private init; }
    public string Currency { get; private init; } = null!;
    public RefundState State { get; private set; }
    public string? Reason { get; private set; }
    public string? ExternalReference { get; private set; }   // gateway's refund id
    public string? FailureReason { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Refund() { } // EF

    public Refund(Guid tenantId, Payment payment, decimal amount, string? reason)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        PaymentId = payment.Id;
        OrderId = payment.OrderId;
        Amount = amount;
        Currency = payment.Currency;
        Reason = reason;
        State = RefundState.Pending;
    }

    public Result MarkSucceeded(string? externalReference = null)
    {
        if (State != RefundState.Pending)
            return PaymentErrors.RefundAlreadyResolved(Id, State);
        State = RefundState.Succeeded;
        ExternalReference = externalReference;
        ResolvedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkFailed(string reason)
    {
        if (State != RefundState.Pending)
            return PaymentErrors.RefundAlreadyResolved(Id, State);
        State = RefundState.Failed;
        FailureReason = reason;
        ResolvedAt = DateTime.UtcNow;
        return Result.Success();
    }
}
