using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

public sealed record ListStaffUsersQuery() : IQuery<IReadOnlyList<StaffUserResponse>>;

public sealed class ListStaffUsersHandler(StoreDbContext db)
    : IRequestHandler<ListStaffUsersQuery, Result<IReadOnlyList<StaffUserResponse>>>
{
    public async Task<Result<IReadOnlyList<StaffUserResponse>>> Handle(
        ListStaffUsersQuery request, CancellationToken ct)
    {
        // StaffUser IS tenant-scoped, so unlike ListStores this only ever sees one store's
        // people — the projection also keeps the password hash out of the result by construction.
        var users = await db.Set<StaffUser>()
            .OrderBy(u => u.Name)
            .Select(u => new StaffUserResponse(u.Id, u.Email, u.Name, u.Role.ToString(), u.IsActive))
            .ToListAsync(ct);

        return users;
    }
}
