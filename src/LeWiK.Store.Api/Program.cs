using LeWiK.Store.Api.Catalog;
using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStoreApp(builder.Configuration.GetConnectionString("Default")!);
builder.Services.AddExceptionHandler<LeWiK.Store.Api.Common.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

//Middlewares
app.UseMiddleware<LeWiK.Store.Api.Tenancy.TenantResolutionMiddleware>();

app.MapGet("/", () => "Hello World!");

app.MapGet("/health/db", async (StoreDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok("db Ok")
        : Results.Problem("db Error"));

app.MapGet("/health/tenant", (ITenantContext tenant) =>
    tenant.HasTenant
        ? Results.Ok(new { tenantId = tenant.TenantId })
        : Results.Ok(new { message = "no tenant resolved" }));

app.MapCatalogEndpoints();

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

app.Run();
