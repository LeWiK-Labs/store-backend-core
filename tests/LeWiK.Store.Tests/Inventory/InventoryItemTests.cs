using LeWiK.Store.App.Inventory.Domain;
using Xunit;

namespace LeWiK.Tienda.Tests.Inventory;

public class InventoryItemTests
{
    private static InventoryItem NewItem() => new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void AddStock_increases_available_and_records_movement()
    {
        var item = NewItem();
        item.AddStock(10);

        Assert.Equal(10, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
        Assert.Single(item.Movements);
        Assert.Equal(StockMovementType.StockIn, item.Movements.First().Type);
    }

    [Fact]
    public void Reserve_moves_available_to_reserved_when_enough()
    {
        var item = NewItem();
        item.AddStock(10);

        var result = item.Reserve(4);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, item.AvailableQuantity);
        Assert.Equal(4, item.ReservedQuantity);
    }

    [Fact]
    public void Reserve_fails_and_keeps_quantities_when_not_enough()
    {
        var item = NewItem();
        item.AddStock(3);

        var result = item.Reserve(5);

        Assert.True(result.IsFailure);
        Assert.Equal("inventory.insufficient_stock", result.Error.Code);
        Assert.Equal(3, item.AvailableQuantity); // unchanged on failure
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Release_returns_reserved_to_available()
    {
        var item = NewItem();
        item.AddStock(10);
        item.Reserve(4);

        item.Release(3);

        Assert.Equal(9, item.AvailableQuantity);
        Assert.Equal(1, item.ReservedQuantity);
    }

    [Fact]
    public void Release_clamps_when_releasing_more_than_reserved()
    {
        var item = NewItem();
        item.AddStock(10);
        item.Reserve(2);

        item.Release(5); // only 2 reserved

        Assert.Equal(10, item.AvailableQuantity);
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Fulfill_consumes_reserved_without_returning_to_available()
    {
        var item = NewItem();
        item.AddStock(10);
        item.Reserve(4);

        var result = item.Fulfill(4);

        Assert.True(result.IsSuccess);
        Assert.Equal(6, item.AvailableQuantity); // not returned
        Assert.Equal(0, item.ReservedQuantity);
    }

    [Fact]
    public void Fulfill_fails_when_more_than_reserved()
    {
        var item = NewItem();
        item.AddStock(10);
        item.Reserve(2);

        var result = item.Fulfill(5);

        Assert.True(result.IsFailure);
        Assert.Equal("inventory.insufficient_reserved", result.Error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void AddStock_throws_on_non_positive(int quantity)
    {
        var item = NewItem();
        Assert.Throws<ArgumentOutOfRangeException>(() => item.AddStock(quantity));
    }
}