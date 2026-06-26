using LeWiK.Store.Api.Common;
using LeWiK.Store.App._Smoke;
using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Tenancy;
using MediatR;

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

// TEMPORARY smoke endpoint — remove in Fase 1
app.MapGet("/ping", async (string? name, ISender sender) =>
    (await sender.Send(new PingQuery(name ?? ""))).ToHttpResult());

app.Run();
