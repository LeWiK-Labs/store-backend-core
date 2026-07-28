using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Payments.Domain;

public enum PaymentType {Full, Deposit, Balance}
public enum PaymentState { Pending, Succeeded, Failed}

public sealed class Payment : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid OrderId { get; private init; }
    public PaymentGateway Gateway { get; private init; }
    public PaymentType Type { get; private init; }
    public decimal Amount { get; private init; }
    public string Currency { get; private init; } = null!;
    public PaymentState State { get; private set; }
    public string? ExternalReference { get; private set; }  // gateway's id (token, payment id)
    public string? FailureReason { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private  Payment() {} //EF
    
    public Payment(Guid tenantId, Guid orderId, PaymentGateway gateway, PaymentType type, decimal amount, string currency, string? externalReference = null)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        OrderId = orderId;
        Gateway = gateway;
        Type = type;
        Amount = amount;
        Currency = currency;
        State = PaymentState.Pending;
        ExternalReference = externalReference;
    }

    // Set while still Pending: gateways return their reference when the charge starts.
    public void SetExternalReference(string reference)
    {
        if (State == PaymentState.Pending)
            ExternalReference = reference;
    }

    public Result MarkSucceeded(string? externalReference = null)
    {
        if (State != PaymentState.Pending)
            return PaymentErrors.AlreadyResolved(Id, State);
        State = PaymentState.Succeeded;
        ExternalReference = externalReference ?? ExternalReference;
        ResolvedAt = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkFailed(string reason)
    {
        if (State != PaymentState.Pending)
            return PaymentErrors.AlreadyResolved(Id, State);
        State = PaymentState.Failed;
        FailureReason = reason;
        ResolvedAt = DateTime.UtcNow;
        return Result.Success();
    }
}