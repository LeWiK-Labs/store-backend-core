using LeWiK.Store.App.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Customers;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.Email).IsRequired().HasMaxLength(320);
        builder.Property(c => c.Phone).IsRequired().HasMaxLength(30);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired(false);
        builder.Property(c => c.IsRegistered).IsRequired();
        builder.Property(c => c.PasswordHash).HasMaxLength(256).IsRequired(false);
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.UpdatedAt).IsRequired();

        // Email unique per tenant — one customer record per email in a store.
        builder.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
    }
}

internal sealed class CustomerSessionConfiguration : IEntityTypeConfiguration<CustomerSession>
{
    public void Configure(EntityTypeBuilder<CustomerSession> b)
    {
        b.ToTable("customer_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.TenantId).IsRequired();
        b.Property(s => s.CustomerId).IsRequired();
        b.HasIndex(s => s.CustomerId);              // "revoke every session for this buyer"
        b.Property(s => s.TokenHash).IsRequired().HasMaxLength(64);
        // The hot path: hit on every authenticated storefront request. Unique because two
        // sessions sharing a token would make revocation ambiguous.
        b.HasIndex(s => s.TokenHash).IsUnique();
        b.Property(s => s.ExpiresAt).IsRequired();
        b.Property(s => s.RevokedAt).IsRequired(false);
        b.Property(s => s.UserAgent).HasMaxLength(400).IsRequired(false);
        b.Property(s => s.CreatedAt).IsRequired();
        b.Property(s => s.UpdatedAt).IsRequired();

        b.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}