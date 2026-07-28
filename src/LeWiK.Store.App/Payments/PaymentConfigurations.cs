using LeWiK.Store.App.Payments.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Payments;

internal sealed class PaymentConfigurations : IEntityTypeConfiguration<PaymentMethodConfig>
{
    public void Configure(EntityTypeBuilder<PaymentMethodConfig> builder)
    {
        builder.ToTable("payment_method_configs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Gateway).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.Gateway }).IsUnique(); // one config per gateway per store
        builder.Property(c => c.EncryptedCredentials).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.OrderId).IsRequired();
        builder.HasIndex(p => p.OrderId);
        builder.Property(p => p.Gateway).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Amount).HasPrecision(14, 4).IsRequired();
        builder.Property(p => p.Currency).IsRequired().HasMaxLength(3);
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.ExternalReference).HasMaxLength(200).IsRequired(false);
        builder.HasIndex(p => p.ExternalReference); // webhooks look payments up by this
        builder.Property(p => p.FailureReason).HasMaxLength(500).IsRequired(false);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        // Pending → Succeeded must happen exactly once even when two duplicate webhooks race:
        // without this, both readers see Pending and both resolve it. uint rowversion mapped
        // to Postgres' xmin system column.
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion();
    }
}