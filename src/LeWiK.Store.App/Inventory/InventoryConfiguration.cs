using LeWiK.Store.App.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Inventory;

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventories");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.TenantId).IsRequired();
        builder.Property(i => i.ProductVariantId).IsRequired();
        builder.HasIndex(i => new { i.TenantId, i.ProductVariantId }).IsUnique(); // one inventory per variant per tenant

        builder.Property(i => i.AvailableQuantity).IsRequired();
        builder.Property(i => i.ReservedQuantity).IsRequired();
        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt).IsRequired();

        // Aggregate: InventoryItem owns its movements (append-only ledger).
        builder.HasMany(i => i.Movements)
            .WithOne()
            .HasForeignKey(m => m.InventoryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Movements).UsePropertyAccessMode(PropertyAccessMode.Field);
        // A uint rowversion maps to Postgres' xmin system column automatically.
        // uint rowversion mapped explicitly to Postgres' xmin system column.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("stock_movements");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.InventoryId).IsRequired();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Quantity).IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500).IsRequired(false);
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasIndex(m => m.InventoryId);
    }
}