using LeWiK.Store.App.Orders.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Orders;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.TenantId).IsRequired();
        builder.HasIndex(o => o.TenantId);
        builder.Property(o => o.CustomerId).IsRequired();
        builder.HasIndex(o => o.CustomerId); // enforcement queries filter by customer

        builder.Property(o => o.FulfillmentStatus).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(o => o.Currency).IsRequired().HasMaxLength(3);
        builder.Property(o => o.TotalAmount).HasPrecision(14, 4).IsRequired();
        builder.Property(o => o.PaidAmount).HasPrecision(14, 4).IsRequired();
        builder.Property(o => o.DepositDueAmount).HasPrecision(14, 4).IsRequired();
        builder.Property(o => o.ReservationExpiresAt);

        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.UpdatedAt).IsRequired();

        // Aggregate: Order owns its lines.
        builder.HasMany(o => o.Lines)
            .WithOne()
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Calculated props (BalanceAmount, Total, Balance) have no setter → EF ignores them.
    }
}

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrderId).IsRequired();
        builder.Property(l => l.ProductVariantId).IsRequired();
        builder.Property(l => l.Sku).IsRequired().HasMaxLength(64);
        builder.Property(l => l.NameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(l => l.QtyOrdered).IsRequired();
        builder.Property(l => l.QtyFulfilled).IsRequired();
        builder.Property(l => l.IsPreorder).IsRequired();

        // UnitPrice is a Money value object → inline columns (currency lives on the order too,
        // but keeping it on the line keeps the snapshot self-contained).
        builder.ComplexProperty(l => l.UnitPrice, price =>
        {
            price.Property(m => m.Amount).HasColumnName("unit_price_amount").HasPrecision(14, 4);
            price.Property(m => m.Currency).HasColumnName("unit_price_currency").HasMaxLength(3);
        });

        builder.HasIndex(l => l.OrderId);
    }
}