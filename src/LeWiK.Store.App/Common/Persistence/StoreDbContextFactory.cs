using LeWiK.Store.App.Common.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeWiK.Store.App.Common.Persistence;

public sealed class StoreDbContextFactory : IDesignTimeDbContextFactory<StoreDbContext>
{
    public StoreDbContext CreateDbContext(string[] args)
    {
        const string connectionString = "Host=localhost;Port=5432;Database=lewik_store;Username=lewik;Password=lewik_dev";
        var options = new DbContextOptionsBuilder<StoreDbContext>()
            .UseNpgsql(connectionString).UseSnakeCaseNamingConvention()
            .Options;

        return new StoreDbContext(options, new DesignTimeTenantContext());
    }

    private sealed class DesignTimeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;
    }
}