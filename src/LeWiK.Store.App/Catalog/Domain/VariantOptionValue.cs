using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed class VariantOptionValue : Entity
{
    public Guid ProductVariantId { get; private init; }
    public Guid ProductOptionValueId { get; private init; }
    
    private VariantOptionValue() { } //EF

    internal VariantOptionValue(Guid productVariantId, Guid productOptionValueId)
    {
        Id = Guid.CreateVersion7();
        ProductVariantId = productVariantId;
        ProductOptionValueId = productOptionValueId;
    }
}