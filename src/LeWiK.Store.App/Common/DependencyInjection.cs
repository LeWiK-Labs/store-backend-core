using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddStoreApp(this IServiceCollection services, string connectionString)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddDbContext<StoreDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}