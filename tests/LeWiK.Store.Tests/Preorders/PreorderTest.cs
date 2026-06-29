using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Preorders.Domain;

namespace LeWiK.Tienda.Tests.Preorders;

public class PreorderTests
{
    private static Preorder NewPreorder(int capacity = 100,
        DepositType type = DepositType.Percentage, decimal value = 30) =>
        new(Guid.NewGuid(), Guid.NewGuid(), capacity, DateTime.UtcNow.AddDays(30), type, value);

    [Fact]
    public void ReserveCapacity_increments_sold_when_available()
    {
        var p = NewPreorder(capacity: 10);
        var result = p.ReserveCapacity(4);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, p.SoldCount);
        Assert.Equal(6, p.AvailableCapacity);
    }

    [Fact]
    public void ReserveCapacity_fails_when_exceeding_capacity()
    {
        var p = NewPreorder(capacity: 3);
        var result = p.ReserveCapacity(5);

        Assert.True(result.IsFailure);
        Assert.Equal("preorder.capacity_exceeded", result.Error.Code);
        Assert.Equal(0, p.SoldCount); // unchanged
    }

    [Fact]
    public void ReserveCapacity_fails_when_closed()
    {
        var p = NewPreorder(capacity: 10);
        p.Close();
        var result = p.ReserveCapacity(1);

        Assert.True(result.IsFailure);
        Assert.Equal("preorder.closed", result.Error.Code);
    }

    [Fact]
    public void ReleaseCapacity_frees_sold_and_clamps_at_zero()
    {
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(3);

        p.ReleaseCapacity(5); // only 3 sold

        Assert.Equal(0, p.SoldCount);
    }

    [Fact]
    public void Reconfigure_fails_when_capacity_below_sold()
    {
        var p = NewPreorder(capacity: 10);
        p.ReserveCapacity(6);

        var result = p.Reconfigure(4, DateTime.UtcNow.AddDays(10), DepositType.None, 0);

        Assert.True(result.IsFailure);
        Assert.Equal("preorder.capacity_below_sold", result.Error.Code);
        Assert.Equal(10, p.Capacity); // unchanged
    }

    [Fact]
    public void CalculateDeposit_percentage_of_total()
    {
        var p = NewPreorder(type: DepositType.Percentage, value: 30);
        var deposit = p.CalculateDeposit(new Money(10000, "CLP"), 2); // total 20000, 30%

        Assert.Equal(6000, deposit.Amount);
        Assert.Equal("CLP", deposit.Currency);
    }

    [Fact]
    public void CalculateDeposit_fixed_per_unit()
    {
        var p = NewPreorder(type: DepositType.FixedPerUnit, value: 5000);
        var deposit = p.CalculateDeposit(new Money(12990, "CLP"), 3); // 5000 * 3

        Assert.Equal(15000, deposit.Amount);
    }

    [Fact]
    public void CalculateDeposit_none_is_full_amount()
    {
        var p = NewPreorder(type: DepositType.None, value: 0);
        var deposit = p.CalculateDeposit(new Money(10000, "CLP"), 2);

        Assert.Equal(20000, deposit.Amount); // pay in full
    }
}