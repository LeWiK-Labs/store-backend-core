using FluentValidation;
using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Messaging.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddStoreApp(this IServiceCollection services, string connectionString, string? redisConnectionString = null)
    {
        //Tenancy
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        //Persistence
        services.AddDbContext<StoreDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        //MediatR pipeline (order matters: outer->inner)
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ConcurrencyRetryBehavior<,>));
            cfg.AddOpenBehavior(typeof(TenantGuardBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
        });
        //Validators
        services.AddValidatorsFromAssembly(assembly);
        //Clients
        //back-office client (stub until the back-office exists)
        services.AddSingleton<IBackOfficeClient, StubBackOfficeClient>();
        
        // Distributed cache: Redis when configured, in-memory fallback otherwise.
        if (!string.IsNullOrEmpty(redisConnectionString))
            services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);
        else services.AddDistributedMemoryCache();
        
        return services;
    }
}