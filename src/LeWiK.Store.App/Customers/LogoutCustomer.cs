using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Customers.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LeWiK.Store.App.Customers;

// ITenantAgnostic, like the staff logout: the token already identifies exactly one session,
// and logging out must never be the thing that fails for want of a resolved store.
public sealed record LogoutCustomerCommand(string Token) : ICommand, ITenantAgnostic;

public sealed class LogoutCustomerHandler(StoreDbContext db, IDistributedCache cache)
    : IRequestHandler<LogoutCustomerCommand, Result>
{
    public async Task<Result> Handle(LogoutCustomerCommand request, CancellationToken ct)
    {
        var hash = OpaqueToken.Hash(request.Token);
        var session = await db.Set<CustomerSession>().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        session?.Revoke();

        // What makes revocation instant: the handler serves from cache, so without dropping
        // the key the session would keep working until the TTL expired.
        await cache.RemoveAsync(SessionCacheKeys.For(hash, SessionAudience.Customer), ct);

        // Idempotent: an unknown or already-revoked token still succeeds.
        return Result.Success();
    }
}
