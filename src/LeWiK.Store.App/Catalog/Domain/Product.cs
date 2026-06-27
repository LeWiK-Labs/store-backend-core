using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Catalog.Domain;

public enum ProductStatus { Active, Inactive}

public sealed class Product : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Money Price { get; private set; }
    public ProductStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private Product() {}

    public Product(Guid tenantId, string sku, string name, string description, Money price)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Sku = sku;
        Name = name;
        Description = description;
        Price = price;
        Status = ProductStatus.Active;
    }
}