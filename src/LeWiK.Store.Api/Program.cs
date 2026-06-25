using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStoreApp(builder.Configuration.GetConnectionString("Default")!);

var app = builder.Build();

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

app.Run();
