using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Preorders.Domain;

public enum DepositType { None, Percentage, FixedPerUnit }
public enum PreorderStatus { Active, Closed }

// An aggregate since 4.7, and only for the event: what falls in front of a buyer during a drop
// is the remaining capacity, not the stock. InventoryItem was promoted in 2.4.1 for the same
// reason and Preorder stayed an Entity, which left the live counter blind in the one case it
// exists for.
public sealed class Preorder : AggregateRoot, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid ProductVariantId { get; private init; }
    public int Capacity { get; private set; }
    public int SoldCount { get; private set; }
    public DateTime ReleaseDate { get; private set; }
    public DepositType DepositType { get; private set; }
    public decimal DepositValue { get; private set; }   // percent (0-100) or per-unit amount
    public PreorderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    public int AvailableCapacity => Capacity - SoldCount;

    private Preorder() { } // EF

    public Preorder(Guid tenantId, Guid productVariantId, int capacity, DateTime releaseDate,
        DepositType depositType, decimal depositValue)
    {
        Id = Guid.CreateVersion7(); // Guid.NewGuid() on .NET 8
        TenantId = tenantId;
        ProductVariantId = productVariantId;
        Capacity = capacity;
        SoldCount = 0;
        ReleaseDate = releaseDate;
        DepositType = depositType;
        DepositValue = depositValue;
        Status = PreorderStatus.Active;
        // Beyond the spec: opening a drop flips how the variant sells, from stock to capacity.
        // Anyone already watching that variant would otherwise keep showing its stock number
        // with no event ever coming to correct it.
        RaiseCapacityChanged();
    }

    // Admin edits an existing drop. Can't drop capacity below what's already sold.
    public Result Reconfigure(int capacity, DateTime releaseDate, DepositType depositType, decimal depositValue)
    {
        // A closed drop is finished. Without this the edit is worse than refused: measured, the
        // PUT answers 200 and writes the new capacity, Status stays Closed, and the storefront
        // keeps selling from stock — an operator is told their drop was reconfigured when nothing
        // a buyer can see has changed.
        //
        // Reopening is deliberately not what this does either, because SoldCount still counts the
        // previous drop: cancelling one of its old orders would hand capacity back to a drop those
        // units were never part of. Re-running a drop on the same variant needs its own decision.
        if (Status == PreorderStatus.Closed)
            return PreorderErrors.Closed();

        if (capacity < SoldCount)
            return PreorderErrors.CapacityBelowSold(capacity, SoldCount);
        Capacity = capacity;
        ReleaseDate = releaseDate;
        DepositType = depositType;
        DepositValue = depositValue;
        RaiseCapacityChanged();
        return Result.Success();
    }

    // Sell against capacity (called when an order is placed — wired in Orders, 2.4).
    public Result ReserveCapacity(int qty)
    {
        RequirePositive(qty);
        if (Status == PreorderStatus.Closed)
            return PreorderErrors.Closed();
        if (qty > AvailableCapacity)
            return PreorderErrors.CapacityExceeded(qty, AvailableCapacity);
        SoldCount += qty;
        RaiseCapacityChanged();
        return Result.Success();
    }

    // Preorder cancelled before release: free the capacity.
    public void ReleaseCapacity(int qty)
    {
        RequirePositive(qty);
        SoldCount = Math.Max(0, SoldCount - qty);
        RaiseCapacityChanged();
    }

    // The drop is over: from here the variant sells from physical stock, because every read and
    // Checkout itself pick an ACTIVE preorder over stock and fall back to stock when there isn't
    // one. Until 4.7.1 nothing called this, so a variant that had ever been a drop stayed a drop
    // forever — its restocked units unreachable behind a capacity counter.
    //
    // Not idempotent on purpose, same as Order.Cancel: closing twice is a second click or a
    // second operator, and the answer to "did I already do this?" should be yes, not silence.
    public Result Close()
    {
        if (Status == PreorderStatus.Closed) return PreorderErrors.AlreadyClosed();
        Status = PreorderStatus.Closed;
        RaiseCapacityChanged();
        return Result.Success();
    }

    // One place, so the four callers cannot disagree about what the event says — in particular
    // that a closed drop is not sellable however much capacity is left on paper.
    private void RaiseCapacityChanged() => Raise(new PreorderCapacityChanged(
        TenantId, ProductVariantId, Capacity, SoldCount, Status == PreorderStatus.Active));

    // Deposit (abono) due for qty units at unitPrice. None → full amount (pay in full).
    public Money CalculateDeposit(Money unitPrice, int qty)
    {
        var total = unitPrice.Multiply(qty);
        return DepositType switch
        {
            DepositType.Percentage => new Money(Math.Round(total.Amount * DepositValue / 100m, 0), total.Currency),
            DepositType.FixedPerUnit => new Money(DepositValue * qty, total.Currency),
            _ => total
        };
    }

    private static void RequirePositive(int qty)
    {
        if (qty <= 0)
            throw new ArgumentOutOfRangeException(nameof(qty), "Quantity must be positive.");
    }
}