using LeWiK.Store.Api.Common;
using LeWiK.Store.App.Platform;
using LeWiK.Store.App.Platform.Domain;
using MediatR;

namespace LeWiK.Store.Api.Platform;

public static class PlatformEndpoints
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        // ⚠️ UNPROTECTED until 4.3 adds the PlatformOperator policy. Anyone who can reach this
        // can create stores and read every store on the platform. Local use only — do not
        // expose this build beyond your machine.
        var platform = app.MapGroup("/platform/stores");

        platform.MapPost("/", async (CreateStoreCommand command, ISender sender) =>
            (await sender.Send(command)).ToHttpResult());

        platform.MapGet("/", async (ISender sender) =>
            (await sender.Send(new ListStoresQuery())).ToHttpResult());

        platform.MapPost("/{storeId:guid}/suspend", async (Guid storeId, ISender sender) =>
            (await sender.Send(new SetStoreStatusCommand(storeId, Active: false))).ToHttpResult());

        platform.MapPost("/{storeId:guid}/activate", async (Guid storeId, ISender sender) =>
            (await sender.Send(new SetStoreStatusCommand(storeId, Active: true))).ToHttpResult());

        // ⚠️ UNPROTECTED until 4.3 adds the StoreStaff policy. Today the X-Tenant-Id header is
        // the only thing deciding which store you are adding people to, and it is unverified.
        var staff = app.MapGroup("/admin/staff");

        staff.MapPost("/", async (CreateStaffBody body, ISender sender) =>
            (await sender.Send(new CreateStaffUserCommand(body.Email, body.Name, body.Password, body.Role)))
                .ToHttpResult());

        staff.MapGet("/", async (ISender sender) =>
            (await sender.Send(new ListStaffUsersQuery())).ToHttpResult());

        return app;
    }
}

public sealed record CreateStaffBody(string Email, string Name, string Password, StaffRole Role);
