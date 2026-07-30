using FluentValidation;
using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Messaging.Behaviors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;
using Microsoft.AspNetCore.DataProtection;

namespace LeWiK.Store.App.Common;

public static class DependencyInjection
{
    public static IServiceCollection AddStoreApp(
        this IServiceCollection services,
        string connectionString,
        string? redisConnectionString = null,
        IConfiguration? configuration = null)
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
        // Entitlement now comes from the local Store table (4.4). Scoped, not singleton:
        // it queries the DbContext, which is scoped.
        services.AddScoped<IBackOfficeClient, Platform.LocalEntitlementService>();

        services.AddScoped<Orders.PurchaseLimitEnforcer>();
        services.AddSingleton<Security.PasswordHasher>();
        
        // Distributed cache: Redis when configured, in-memory fallback otherwise.
        if (!string.IsNullOrEmpty(redisConnectionString))
            services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);
        else services.AddDistributedMemoryCache();
        
        services.AddDataProtection().PersistKeysToDbContext<StoreDbContext>();
        services.AddSingleton<Payments.CredentialProtector>();
        
        services.AddScoped<Payments.IPaymentGatewayClient, Payments.Gateways.TransferGatewayClient>();
        services.AddScoped<Payments.IPaymentGatewayClient, Payments.Gateways.WebpayGatewayClient>();
        services.AddScoped<Payments.IPaymentGatewayClient, Payments.Gateways.MercadoPagoGatewayClient>();
        services.AddScoped<Payments.PaymentGatewayResolver>();

        services.Configure<Payments.PaymentSettings>(o =>
            configuration?.GetSection("Payments").Bind(o));

        services.Configure<Platform.TenancySettings>(o =>
            configuration?.GetSection("Tenancy").Bind(o));
        services.AddScoped<Platform.StoreResolver>();

        return services;
    }
}