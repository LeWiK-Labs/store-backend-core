using LeWiK.Store.App.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Catalog;

internal sealed class PurchaseLimitConfiguration : IEntityTypeConfiguration<PurchaseLimit>
{
    public void Configure(EntityTypeBuilder<PurchaseLimit> builder)
    {
        builder.ToTable("purchase_limits");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.TenantId).IsRequired();
        builder.Property(l => l.Scope).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.TargetId).IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.Scope, l.TargetId }).IsUnique();

        builder.Property(l => l.MaxPerOrder);
        builder.Property(l => l.MaxPerCustomer);
        builder.Property(l => l.WindowDays);
        builder.Property(l => l.CreatedAt).IsRequired();
        builder.Property(l => l.UpdatedAt).IsRequired();
    }
}