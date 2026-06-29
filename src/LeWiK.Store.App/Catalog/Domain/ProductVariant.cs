using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Catalog.Domain;

public sealed class ProductVariant : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public Guid ProductId { get; private init; }
    public string Sku { get; private set; } = null!;
    public string Label { get; private set; } = null!; // "Default", "English", "Japanese", etc...
    public Money Price { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<VariantOptionValue> _optionValues = [];
    public IReadOnlyCollection<VariantOptionValue> OptionValues => _optionValues.AsReadOnly();
    
    private ProductVariant() {} // EF

    private ProductVariant(Guid tenantId, Guid productId, string sku, string label, Money price)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        ProductId = productId;
        Sku = sku;
        Label = label;
        Price = price;
    }

    internal static ProductVariant CreateDefault(Product p, string sku, Money price) =>
        new(p.TenantId, p.Id, sku, "Default", price);
    
    internal static ProductVariant Create(Product p, string sku, string label, Money price) =>
        new(p.TenantId, p.Id, sku, label, price);
    
    internal void LinkOptionValue(Guid optionValueId) => _optionValues.Add(new VariantOptionValue(Id, optionValueId));
}