using System.Reflection;
using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Common.Persistence;

public class StoreDbContext(DbContextOptions<StoreDbContext> options, ITenantContext tenantContext) : DbContext(options)
{
    private readonly ITenantContext _tenantContext = tenantContext;

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(StoreDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic);
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(AggregateRoot).IsAssignableFrom(entityType.ClrType))
                modelBuilder.Entity(entityType.ClrType).Ignore(nameof(AggregateRoot.DomainEvents));

            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
                entry.Property(nameof(IAuditable.CreatedAt)).CurrentValue = now;
            
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(nameof(IAuditable.UpdatedAt)).CurrentValue = now;
        }
        return base.SaveChangesAsync(ct);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);
    }
}