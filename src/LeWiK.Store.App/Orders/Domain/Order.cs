using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Orders.Domain;

public sealed class Order : AggregateRoot, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid CustomerId { get; private init; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string Currency { get; private init; } = null!;
    public decimal TotalAmount { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal DepositDueAmount { get; private set; }   // required to move forward (abono); == total if pay-in-full
    public DateTime? ReservationExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<OrderLine> _lines = [];
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public decimal BalanceAmount => TotalAmount - PaidAmount;
    public Money Total => new(TotalAmount, Currency);
    public Money Balance => new(BalanceAmount, Currency);
    
    private Order() { } // EF

    private Order(Guid tenantId, Guid customerId, string currency)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        CustomerId = customerId;
        Currency = currency;
        FulfillmentStatus = FulfillmentStatus.PendingPayment;
        PaymentStatus = PaymentStatus.Pending;
    }

    // ---- Construction ----
    // Built by the checkout handler, which resolves prices/deposit/preorder externally.
    public static Result<Order> Place(
        Guid tenantId, Guid customerId, string currency,
        IReadOnlyList<OrderLineDraft> lines, decimal depositDueAmount)
    {
        if (lines.Count == 0)
            return OrderErrors.EmptyOrder();
        if (lines.Any(l => l.Currency != currency))
            return OrderErrors.MixedCurrency();

        var order = new Order(tenantId, customerId, currency);
        foreach (var d in lines)
            order._lines.Add(new OrderLine(order.Id, d.ProductVariantId, d.Sku, d.NameSnapshot,
                new Money(d.UnitPrice, currency), d.Quantity, d.IsPreorder));

        order.TotalAmount = order._lines.Sum(l => l.LineTotal.Amount);

        // Deposit must be within (0, total]. 0 or >= total means pay-in-full.
        order.DepositDueAmount = depositDueAmount <= 0 || depositDueAmount >= order.TotalAmount
            ? order.TotalAmount
            : depositDueAmount;
        
        order.ReservationExpiresAt = null;

        order.Raise(new OrderPlaced(tenantId, order.Id, customerId));
        return order;
    }

    // ---- Payment (orthogonal axis) ----
    public Result ApplyPayment(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment must be positive.");
        if (FulfillmentStatus == FulfillmentStatus.Cancelled)
            return OrderErrors.OrderCancelled();
        if (PaymentStatus == PaymentStatus.Paid)
            return OrderErrors.AlreadyPaid();
        if (amount > BalanceAmount)
            return OrderErrors.PaymentExceedsBalance(amount, BalanceAmount);

        PaidAmount += amount;
        RecalculatePaymentStatus();
        return Result.Success();
    }

    private void RecalculatePaymentStatus()
    {
        var previous = PaymentStatus;
        PaymentStatus = PaidAmount >= TotalAmount ? PaymentStatus.Paid
            : PaidAmount >= DepositDueAmount && DepositDueAmount < TotalAmount ? PaymentStatus.Deposited
            : PaidAmount > 0 ? PaymentStatus.Deposited
            : PaymentStatus.Pending;

        if (PaymentStatus == previous) return;

        if (PaymentStatus == PaymentStatus.Paid)
            Raise(new OrderPaid(TenantId, Id, CustomerId));
        else if (PaymentStatus == PaymentStatus.Deposited)
            Raise(new OrderDeposited(TenantId, Id, CustomerId));

        AdvanceAfterPayment();
    }

    // When payment clears the required threshold, move fulfillment forward.
    private void AdvanceAfterPayment()
    {
        if (FulfillmentStatus != FulfillmentStatus.PendingPayment) return;
        if (PaymentStatus == PaymentStatus.Pending) return;

        // Deposit is enough to move a preorder into AwaitingRelease.
        // Full payment with no preorder lines → ready to prepare (Paid).
        var hasPreorder = _lines.Any(l => l.IsPreorder);
        FulfillmentStatus = hasPreorder ? FulfillmentStatus.AwaitingRelease
            : PaymentStatus == PaymentStatus.Paid ? FulfillmentStatus.Paid
            : FulfillmentStatus.AwaitingRelease;
    }

    // Preorder stock arrived: release for preparation (requires full payment).
    public Result MarkReleased()
    {
        if (FulfillmentStatus != FulfillmentStatus.AwaitingRelease)
            return OrderErrors.InvalidTransition(FulfillmentStatus, nameof(MarkReleased));
        if (PaymentStatus != PaymentStatus.Paid)
            return OrderErrors.BalancePending();
        FulfillmentStatus = FulfillmentStatus.Paid;
        return Result.Success();
    }

    // ---- Fulfillment (orthogonal axis, line-level) ----
    public Result StartPreparing()
    {
        if (FulfillmentStatus != FulfillmentStatus.Paid)
            return OrderErrors.InvalidTransition(FulfillmentStatus, nameof(StartPreparing));
        FulfillmentStatus = FulfillmentStatus.Preparing;
        return Result.Success();
    }

    // Hand over units of a specific line (pickup/shipment). Order status derives from lines.
    public Result FulfillLine(Guid orderLineId, int qty)
    {
        if (FulfillmentStatus is not (FulfillmentStatus.Preparing or FulfillmentStatus.PartiallyDelivered))
            return OrderErrors.InvalidTransition(FulfillmentStatus, nameof(FulfillLine));
        if (PaymentStatus != PaymentStatus.Paid)
            return OrderErrors.BalancePending();

        var line = _lines.FirstOrDefault(l => l.Id == orderLineId);
        if (line is null)
            return OrderErrors.LineNotFound(orderLineId);

        var result = line.Fulfill(qty);
        if (result.IsFailure) return result;

        FulfillmentStatus = _lines.All(l => l.IsFullyFulfilled)
            ? FulfillmentStatus.Delivered
            : FulfillmentStatus.PartiallyDelivered;

        if (FulfillmentStatus == FulfillmentStatus.Delivered)
            Raise(new OrderDelivered(TenantId, Id, CustomerId));
        return Result.Success();
    }

    // ---- Cancellation ----
    public Result Cancel()
    {
        if (FulfillmentStatus == FulfillmentStatus.Delivered)
            return OrderErrors.CannotCancelDelivered();
        if (FulfillmentStatus == FulfillmentStatus.Cancelled)
            return OrderErrors.OrderCancelled();
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        Raise(new OrderCancelled(TenantId, Id, CustomerId));
        return Result.Success();
    }
}