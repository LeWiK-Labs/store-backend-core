using LeWiK.Store.App.Preorders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Preorders;

internal sealed class PreorderConfiguration : IEntityTypeConfiguration<Preorder>
{
    public void Configure(EntityTypeBuilder<Preorder> b)
    {
        b.ToTable("preorders");
        b.HasKey(p => p.Id);

        b.Property(p => p.TenantId).IsRequired();
        b.Property(p => p.ProductVariantId).IsRequired();
        b.HasIndex(p => new { p.TenantId, p.ProductVariantId }).IsUnique(); // one preorder per variant

        b.Property(p => p.Capacity).IsRequired();
        b.Property(p => p.SoldCount).IsRequired();
        b.Property(p => p.ReleaseDate).IsRequired();
        b.Property(p => p.DepositType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(p => p.DepositValue).HasPrecision(14, 4).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(p => p.CreatedAt).IsRequired();
        b.Property(p => p.UpdatedAt).IsRequired();
    }
}