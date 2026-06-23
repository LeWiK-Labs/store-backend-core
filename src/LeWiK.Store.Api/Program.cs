using LeWiK.Store.App.Common;
using LeWiK.Store.App.Common.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStoreApp(builder.Configuration.GetConnectionString("Default")!);

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.MapGet("/health/db", async (StoreDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok("db Ok")
        : Results.Problem("db Error"));

app.Run();
