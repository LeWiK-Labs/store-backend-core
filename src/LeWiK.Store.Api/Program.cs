using LeWiK.Store.Api.Catalog;
using LeWiK.Store.Api.Inventory;
using LeWiK.Store.Api.Orders;
using LeWiK.Store.Api.Payments;
using LeWiK.Store.Api.Preorders;
using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

var builder = WebApplication.CreateBuilder(args);
var redisConnection = builder.Configuration.GetConnectionString("Redis");

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

builder.Services.AddStoreApp(builder.Configuration.GetConnectionString("Default")!, redisConnection);
builder.Services.AddExceptionHandler<LeWiK.Store.Api.Common.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var signalR = builder.Services.AddSignalR();
if(!string.IsNullOrWhiteSpace(redisConnection)) signalR.AddStackExchangeRedis(redisConnection);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new LeWiK.Store.Api.Common.TrimmedDecimalConverter());
});

var app = builder.Build();

// Apply pending migrations on startup (containerized dev convenience; off by default,
// enabled via Database__MigrateOnStartup env var in docker-compose only).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<LeWiK.Store.App.Common.Persistence.StoreDbContext>()
        .Database.Migrate();
}

app.UseExceptionHandler();
app.UseCors("Frontend");

//Middlewares
app.UseMiddleware<LeWiK.Store.Api.Tenancy.TenantResolutionMiddleware>();

app.MapGet("/", () => "Hello World!");

app.MapGet("/health/db", async (StoreDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok("db ok")
        : Results.Problem("db Error"));

app.MapGet("/health/tenant", (ITenantContext tenant) =>
    tenant.HasTenant
        ? Results.Ok(new { tenantId = tenant.TenantId })
        : Results.Ok(new { message = "no tenant resolved" }));

app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapPreorderEndpoints();
app.MapOrderEndpoints();
app.MapPaymentEndpoints();

// TEMPORARY smoke endpoint — remove once entitlement is enforced for real
app.MapGet("/health/entitlement", async (ITenantContext tenant, IBackOfficeClient backOffice) =>
{
    if (!tenant.HasTenant)
        return Results.Ok(new { message = "no tenant resolved" });

    var entitlement = await backOffice.GetEntitlementAsync(tenant.TenantId);
    return entitlement is null
        ? Results.NotFound(new { message = "unknown tenant" })
        : Results.Ok(entitlement);
});

// exercises the real Redis connection (set + get round-trip)
app.MapGet("/health/cache", async (IDistributedCache cache) =>
{
    await cache.SetStringAsync("health:ping", "ok");
    return Results.Ok(new { cache = await cache.GetStringAsync("health:ping") });
});

app.MapHub<LeWiK.Store.Api.Realtime.StoreHub>("/hubs/store");

app.Run();
