using LeWiK.Store.App.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Catalog;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.TenantId).IsRequired();
        builder.HasIndex(p => p.TenantId);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(2000).IsRequired(false);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p=> p.CreatedAt).IsRequired();
        builder.Property(p=> p.UpdatedAt).IsRequired();

        builder.HasMany(p => p.Variants).WithOne().HasForeignKey(v => v.ProductId).OnDelete(DeleteBehavior.Cascade);
        
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("product_variants");
        
        builder.HasKey(v => v.Id);
        
        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.ProductId).IsRequired();
        builder.Property(v => v.Sku).IsRequired().HasMaxLength(64);
        builder.Property(v => v.Label).IsRequired().HasMaxLength(100);

        builder.ComplexProperty(v => v.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(14, 4);
            price.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        
        builder.Property(v => v.CreatedAt).IsRequired();
        builder.Property(v => v.UpdatedAt).IsRequired();
    }
}