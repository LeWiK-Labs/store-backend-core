using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed class ProductOptionValue : Entity
{
    public Guid ProductOptionId { get; private init; }
    public string Value { get; private set; } = null!;
    public int Position { get; private set; }
    
    private ProductOptionValue() {} //EF
    
    internal ProductOptionValue(Guid productOptionId, string value, int position)
    {
        Id = Guid.CreateVersion7();
        ProductOptionId = productOptionId;
        Value = value;
        Position = position;
    }
}