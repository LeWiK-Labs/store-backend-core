using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LeWiK.Store.App.Platform;

public sealed record SetStaffUserStatusCommand(Guid StaffUserId, bool Active) : ICommand<StaffUserResponse>;

public sealed class SetStaffUserStatusHandler(StoreDbContext db, IDistributedCache cache)
    : IRequestHandler<SetStaffUserStatusCommand, Result<StaffUserResponse>>
{
    public async Task<Result<StaffUserResponse>> Handle(SetStaffUserStatusCommand request, CancellationToken ct)
    {
        // Tenant-filtered: one store cannot deactivate another store's people.
        var user = await db.Set<StaffUser>().FirstOrDefaultAsync(u => u.Id == request.StaffUserId, ct);
        if (user is null) return PlatformErrors.StaffNotFound(request.StaffUserId);

        if (request.Active)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();

            // Without this the sessions would still die — the authentication handler joins on
            // IsActive — but only once their cached entries expired, up to a minute later. A
            // fired cashier keeping the POS for another minute is exactly the gap that matters,
            // so the sessions are revoked and their cache keys dropped right now.
            var sessions = await db.Set<StaffSession>()
                .Where(s => s.StaffUserId == user.Id && s.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var session in sessions)
            {
                session.Revoke();
                await cache.RemoveAsync(SessionCacheKeys.For(session.TokenHash, isPlatform: false), ct);
            }
        }

        return new StaffUserResponse(user.Id, user.Email, user.Name, user.Role.ToString(), user.IsActive);
    }
}
