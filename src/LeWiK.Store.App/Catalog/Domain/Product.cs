using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Catalog.Domain;

public enum ProductStatus { Active, Inactive}

public sealed class Product : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private readonly List<ProductVariant> _variants = [];
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();
    
    private Product() {}

    public Product(Guid tenantId, string name, string? description, string defaultSku, Money defaultPrice)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Name = name;
        Description = description;
        Status = ProductStatus.Active;
        _variants.Add(ProductVariant.CreateDefault(this, defaultSku, defaultPrice));
    }

    public ProductVariant AddVariant(string sku, string label, Money price)
    {
        var variant = ProductVariant.Create(this, sku, label, price);
        _variants.Add(variant);
        return variant;
    }
}