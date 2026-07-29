using LeWiK.Store.App.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LeWiK.Store.App.Platform;

internal sealed class StoreConfiguration : IEntityTypeConfiguration<Domain.Store>
{
    public void Configure(EntityTypeBuilder<Domain.Store> b)
    {
        b.ToTable("stores");
        b.HasKey(s => s.Id);
        b.Property(s => s.Name).IsRequired().HasMaxLength(200);
        b.Property(s => s.Slug).IsRequired().HasMaxLength(63);      // DNS label limit
        b.HasIndex(s => s.Slug).IsUnique();
        b.Property(s => s.CustomDomain).HasMaxLength(253).IsRequired(false);
        // Filtered so every store WITHOUT a custom domain doesn't collide on NULL.
        b.HasIndex(s => s.CustomDomain).IsUnique().HasFilter("custom_domain IS NOT NULL");
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(s => s.CreatedAt).IsRequired();
        b.Property(s => s.UpdatedAt).IsRequired();
    }
}

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> b)
    {
        b.ToTable("staff_users");
        b.HasKey(u => u.Id);
        b.Property(u => u.TenantId).IsRequired();
        b.Property(u => u.Email).IsRequired().HasMaxLength(320);
        // The same person can work at two stores under one email, so uniqueness is per store.
        b.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
        b.Property(u => u.Name).IsRequired().HasMaxLength(200);
        b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(u => u.IsActive).IsRequired();
        b.Property(u => u.LastLoginAt).IsRequired(false);
        b.Property(u => u.CreatedAt).IsRequired();
        b.Property(u => u.UpdatedAt).IsRequired();

        // The first real foreign key to the tenant. Until now TenantId was an unbacked GUID
        // that could point at nothing; stores exist now, so integrity can be enforced.
        b.HasOne<Domain.Store>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlatformOperatorConfiguration : IEntityTypeConfiguration<PlatformOperator>
{
    public void Configure(EntityTypeBuilder<PlatformOperator> b)
    {
        b.ToTable("platform_operators");
        b.HasKey(o => o.Id);
        b.Property(o => o.Email).IsRequired().HasMaxLength(320);
        b.HasIndex(o => o.Email).IsUnique();                       // global: there is no tenant
        b.Property(o => o.Name).IsRequired().HasMaxLength(200);
        b.Property(o => o.PasswordHash).IsRequired().HasMaxLength(256);
        b.Property(o => o.IsActive).IsRequired();
        b.Property(o => o.LastLoginAt).IsRequired(false);
        b.Property(o => o.CreatedAt).IsRequired();
        b.Property(o => o.UpdatedAt).IsRequired();
    }
}
