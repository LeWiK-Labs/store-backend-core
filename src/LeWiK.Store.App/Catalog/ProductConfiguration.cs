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
        
        builder.Property(p => p.Sku).IsRequired().HasMaxLength(64);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(2000);
        
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        
        // Money value object → inline columns, no separate identity
        builder.ComplexProperty(p => p.Price, price =>
        {
            price.Property(m => m.Amount).HasColumnName("price_amount").HasPrecision(14, 4);
            price.Property(m => m.Currency).HasColumnName("price_currency").HasMaxLength(3);
        });
        
        builder.Property(p=> p.CreatedAt).IsRequired();
        builder.Property(p=> p.UpdatedAt).IsRequired();
        
        builder.HasIndex(p => new { p.TenantId, p.Sku }).IsUnique();
    }
}