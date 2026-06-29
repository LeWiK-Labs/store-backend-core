using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Preorders.Domain;

public enum DepositType { None, Percentage, FixedPerUnit }
public enum PreorderStatus { Active, Closed }

public sealed class Preorder : Entity, ITenantScoped, IAuditable
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
    }

    // Admin edits an existing drop. Can't drop capacity below what's already sold.
    public Result Reconfigure(int capacity, DateTime releaseDate, DepositType depositType, decimal depositValue)
    {
        if (capacity < SoldCount)
            return PreorderErrors.CapacityBelowSold(capacity, SoldCount);
        Capacity = capacity;
        ReleaseDate = releaseDate;
        DepositType = depositType;
        DepositValue = depositValue;
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
        return Result.Success();
    }

    // Preorder cancelled before release: free the capacity.
    public void ReleaseCapacity(int qty)
    {
        RequirePositive(qty);
        SoldCount = Math.Max(0, SoldCount - qty);
    }

    public void Close() => Status = PreorderStatus.Closed;

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