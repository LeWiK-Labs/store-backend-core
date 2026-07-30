using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Catalog;
using LeWiK.Store.Api.Customers;
using LeWiK.Store.Api.Inventory;
using LeWiK.Store.Api.Orders;
using LeWiK.Store.Api.Payments;
using LeWiK.Store.Api.Platform;
using LeWiK.Store.Api.Preorders;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Platform.Domain;
using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Security;
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
        .AllowAnyMethod()
        // Session cookies travel cross-origin from the panel, and credentials are incompatible
        // with a wildcard origin — which is why the explicit allow-list has been there from
        // the start. The front must send credentials: 'include'.
        .AllowCredentials());
});

builder.Services.AddStoreApp(builder.Configuration.GetConnectionString("Default")!, redisConnection, builder.Configuration);
builder.Services.AddStoreAuth();
builder.Services.AddExceptionHandler<LeWiK.Store.Api.Common.GlobalExceptionHandler>();
builder.Services.AddHostedService<LeWiK.Store.Api.Workers.ReservationExpiryWorker>();
builder.Services.AddProblemDetails();

var signalR = builder.Services.AddSignalR();
if(!string.IsNullOrWhiteSpace(redisConnection)) signalR.AddStackExchangeRedis(redisConnection);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new LeWiK.Store.Api.Common.TrimmedDecimalConverter());
});

var app = builder.Build();

// Refuse to start with the tenant header override enabled outside Development: it would let
// anyone operate any store by sending X-Tenant-Id. Crashing on boot is the point — a
// misconfiguration this severe must not survive as a warning nobody reads.
if (!app.Environment.IsDevelopment()
    && app.Configuration.GetValue<bool>("Tenancy:AllowHeaderOverride"))
{
    throw new InvalidOperationException(
        "Tenancy:AllowHeaderOverride must be false outside Development.");
}

// Apply pending migrations on startup (containerized dev convenience; off by default,
// enabled via Database__MigrateOnStartup env var in docker-compose only).
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<LeWiK.Store.App.Common.Persistence.StoreDbContext>()
        .Database.Migrate();
}

// Bootstrap the first platform operator — the chicken-and-egg exit, since creating operators
// will require being one. Config-gated: set the values once, start, then remove them. Does
// nothing if any operator already exists, so it is safe to leave configured by accident.
var seedEmail = app.Configuration["Platform:SeedOperatorEmail"];
var seedPassword = app.Configuration["Platform:SeedOperatorPassword"];
if (!string.IsNullOrWhiteSpace(seedEmail) && !string.IsNullOrWhiteSpace(seedPassword))
{
    using var seedScope = app.Services.CreateScope();
    var seedDb = seedScope.ServiceProvider.GetRequiredService<StoreDbContext>();
    if (!await seedDb.Set<PlatformOperator>().AnyAsync())
    {
        var hasher = seedScope.ServiceProvider.GetRequiredService<PasswordHasher>();
        seedDb.Add(new PlatformOperator(seedEmail, "Platform Admin", hasher.Hash(seedPassword)));
        await seedDb.SaveChangesAsync();
    }
}

app.UseExceptionHandler();
app.UseCors("Frontend");

//Middlewares
app.UseMiddleware<LeWiK.Store.Api.Tenancy.TenantResolutionMiddleware>();

// Order matters: the tenant has to be resolved before TenantMatchRequirement can compare the
// session's store against the requested one.
app.UseAuthentication();
app.UseAuthorization();

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
app.MapPaymentLinkEndpoints();
app.MapPlatformEndpoints();
app.MapAuthEndpoints();
app.MapCustomerAuthEndpoints();

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
