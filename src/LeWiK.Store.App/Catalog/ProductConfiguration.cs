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
        
        builder.HasMany(p => p.Options).WithOne().HasForeignKey(o => o.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
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
        
        builder.HasMany(v => v.OptionValues).WithOne().HasForeignKey(ov => ov.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.OptionValues).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductOptionConfiguration : IEntityTypeConfiguration<ProductOption>
{
    public void Configure(EntityTypeBuilder<ProductOption> b)
    {
        b.ToTable("product_options");
        b.HasKey(o => o.Id);
        b.Property(o => o.ProductId).IsRequired();
        b.Property(o => o.Name).IsRequired().HasMaxLength(100);
        b.Property(o => o.Position).IsRequired();
        b.HasIndex(o => o.ProductId);

        b.HasMany(o => o.Values)
            .WithOne()
            .HasForeignKey(v => v.ProductOptionId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Values).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ProductOptionValueConfiguration : IEntityTypeConfiguration<ProductOptionValue>
{
    public void Configure(EntityTypeBuilder<ProductOptionValue> b)
    {
        b.ToTable("product_option_values");
        b.HasKey(v => v.Id);
        b.Property(v => v.ProductOptionId).IsRequired();
        b.Property(v => v.Value).IsRequired().HasMaxLength(100);
        b.Property(v => v.Position).IsRequired();
        b.HasIndex(v => v.ProductOptionId);
    }
}

internal sealed class VariantOptionValueConfiguration : IEntityTypeConfiguration<VariantOptionValue>
{
    public void Configure(EntityTypeBuilder<VariantOptionValue> b)
    {
        b.ToTable("variant_option_values");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductVariantId).IsRequired();
        b.Property(x => x.ProductOptionValueId).IsRequired();

        // Cascade comes from the variant side; the value side is Restrict to avoid
        // multiple cascade paths and accidental deletes of links.
        b.HasOne<ProductOptionValue>()
            .WithMany()
            .HasForeignKey(x => x.ProductOptionValueId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.ProductVariantId, x.ProductOptionValueId }).IsUnique();
    }
}