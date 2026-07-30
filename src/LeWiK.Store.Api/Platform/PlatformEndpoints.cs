using LeWiK.Store.Api.Auth;
using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Platform.Domain;
using MediatR;

namespace LeWiK.Store.Api.Platform;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        // LeWiK only: creating stores and reading every store on the platform.
        var platform = app.MapGroup("/platform/stores")
            .RequireAuthorization(AuthPolicies.PlatformOperator)
            .RequireCsrfHeader();

        platform.MapPost("/", async (CreateStoreCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        platform.MapGet("/", async (ISender sender) =>
            (await sender.Send(new ListStoresQuery())).ToHttpResult());

        platform.MapPost("/{storeId:guid}/suspend", async (Guid storeId, ISender sender) =>
            (await sender.Send(new SetStoreStatusCommand(storeId, Active: false))).ToHttpResult());

        platform.MapPost("/{storeId:guid}/activate", async (Guid storeId, ISender sender) =>
            (await sender.Send(new SetStoreStatusCommand(storeId, Active: true))).ToHttpResult());

        // Admin-level, not plain staff: a cashier does not get to create users — least of all
        // one with a role above their own.
        var staff = app.MapGroup("/admin/staff")
            .RequireAuthorization(AuthPolicies.StoreAdmin)
            .RequireCsrfHeader();

        staff.MapPost("/", async (CreateStaffBody body, ISender sender) =>
            (await sender.Send(new CreateStaffUserCommand(body.Email, body.Name, body.Password, body.Role)))
                .ToHttpResult());

        staff.MapGet("/", async (ISender sender) =>
            (await sender.Send(new ListStaffUsersQuery())).ToHttpResult());

        // Deactivation kills their live sessions on the spot, not when a cache entry expires.
        staff.MapPost("/{staffUserId:guid}/deactivate", async (Guid staffUserId, ISender sender) =>
            (await sender.Send(new SetStaffUserStatusCommand(staffUserId, Active: false))).ToHttpResult());
        staff.MapPost("/{staffUserId:guid}/activate", async (Guid staffUserId, ISender sender) =>
            (await sender.Send(new SetStaffUserStatusCommand(staffUserId, Active: true))).ToHttpResult());

        return app;
    }
}

public sealed record CreateStaffBody(string Email, string Name, string Password, StaffRole Role);
