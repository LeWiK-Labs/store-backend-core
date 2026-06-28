using LeWiK.Store.App.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Inventory;

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("inventories");
        b.HasKey(i => i.Id);

        b.Property(i => i.TenantId).IsRequired();
        b.Property(i => i.ProductVariantId).IsRequired();
        b.HasIndex(i => new { i.TenantId, i.ProductVariantId }).IsUnique(); // one inventory per variant per tenant

        b.Property(i => i.AvailableQuantity).IsRequired();
        b.Property(i => i.ReservedQuantity).IsRequired();
        b.Property(i => i.CreatedAt).IsRequired();
        b.Property(i => i.UpdatedAt).IsRequired();

        // Aggregate: InventoryItem owns its movements (append-only ledger).
        b.HasMany(i => i.Movements)
            .WithOne()
            .HasForeignKey(m => m.InventoryId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements");
        b.HasKey(m => m.Id);

        b.Property(m => m.InventoryId).IsRequired();
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(m => m.Quantity).IsRequired();
        b.Property(m => m.Reason).HasMaxLength(500).IsRequired(false);
        b.Property(m => m.CreatedAt).IsRequired();

        b.HasIndex(m => m.InventoryId);
    }
}