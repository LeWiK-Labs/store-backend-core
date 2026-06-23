using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LeWiK.Store.App.Common.Persistence;

namespace LeWiK.Store.App.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddStoreApp(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<StoreDbContext>(o => o.UseNpgsql(connectionString));
        return services;
    }
}