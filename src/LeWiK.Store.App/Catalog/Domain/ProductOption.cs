using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed class ProductOption : Entity
{
    public Guid ProductId { get; private init; }
    public string Name { get; private set; } = null!;
    public int Position { get; private set; }

    private readonly List<ProductOptionValue> _values = [];
    public IReadOnlyCollection<ProductOptionValue> Values => _values.AsReadOnly();
    
    private ProductOption() {} //EF

    internal ProductOption(Guid productId, string name, int position, IEnumerable<string> values)
    {
        Id = Guid.CreateVersion7();
        ProductId = productId;
        Name = name;
        Position = position;
        var pos = 0;
        foreach (var v in values)
            _values.Add(new ProductOptionValue(Id, v, pos++));
    }
}