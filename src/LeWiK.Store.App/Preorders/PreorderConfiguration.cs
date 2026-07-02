using LeWiK.Store.App.Preorders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Preorders;

internal sealed class PreorderConfiguration : IEntityTypeConfiguration<Preorder>
{
    public void Configure(EntityTypeBuilder<Preorder> builder)
    {
        builder.ToTable("preorders");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.ProductVariantId).IsRequired();
        builder.HasIndex(p => new { p.TenantId, p.ProductVariantId }).IsUnique(); // one preorder per variant

        builder.Property(p => p.Capacity).IsRequired();
        builder.Property(p => p.SoldCount).IsRequired();
        builder.Property(p => p.ReleaseDate).IsRequired();
        builder.Property(p => p.DepositType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.DepositValue).HasPrecision(14, 4).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();
        // uint rowversion mapped explicitly to Postgres' xmin system column.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();
    }
}