using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using LeWiK.Store.App.Common.Security;
using LeWiK.Store.App.Platform.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace LeWiK.Store.App.Platform;

// ITenantAgnostic: the session is found by token hash, and a platform logout has no tenant at
// all. Logging out must never be the thing that fails for want of a header.
public sealed record LogoutCommand(string Token, bool IsPlatform) : ICommand, ITenantAgnostic;

public sealed class LogoutHandler(StoreDbContext db, IDistributedCache cache)
    : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        var hash = OpaqueToken.Hash(request.Token);

        if (request.IsPlatform)
        {
            var session = await db.Set<PlatformSession>().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
            session?.Revoke();
        }
        else
        {
            // Not tenant-filtered, so a logout works even if the tenant header is missing or
            // wrong — the token already identifies exactly one session.
            var session = await db.Set<StaffSession>().FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
            session?.Revoke();
        }

        // This line is what makes revocation instant. The authentication handler serves from
        // cache, so without dropping the key the session would keep working until the TTL
        // expired — which is exactly the window a POS logout cannot afford.
        await cache.RemoveAsync(SessionCacheKeys.For(hash, request.IsPlatform), ct);

        // Unknown or already-revoked tokens still succeed: logout is idempotent, and telling
        // a caller their token was unrecognised is information they have no use for.
        return Result.Success();
    }
}

public static class SessionCacheKeys
{
    public static string For(string tokenHash, bool isPlatform) =>
        $"session:{(isPlatform ? "platform" : "staff")}:{tokenHash}";
}
