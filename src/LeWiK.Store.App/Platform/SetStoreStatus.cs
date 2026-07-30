using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace LeWiK.Store.App.Platform;

// ITenantAgnostic: acts ON a store from outside it. The target is the argument, not the
// ambient tenant — an operator suspending a store is never "inside" that store.
public sealed record SetStoreStatusCommand(Guid StoreId, bool Active)
    : ICommand<StoreResponse>, ITenantAgnostic;

public sealed class SetStoreStatusHandler(
    StoreDbContext db, IDistributedCache cache, IOptions<TenancySettings> settings)
    : IRequestHandler<SetStoreStatusCommand, Result<StoreResponse>>
{
    public async Task<Result<StoreResponse>> Handle(SetStoreStatusCommand request, CancellationToken ct)
    {
        var store = await db.Set<Domain.Store>().FirstOrDefaultAsync(s => s.Id == request.StoreId, ct);
        if (store is null) return PlatformErrors.StoreNotFound(request.StoreId);

        if (request.Active) store.Activate(); else store.Suspend();

        // Drop every cached host->store entry for this store so suspension takes effect on the
        // next request instead of up to a TTL later. Same pattern as revoking a session: the
        // cache is an optimisation, and an optimisation must never outlive the decision.
        //
        // This runs before the commit (UnitOfWorkBehavior saves after the handler returns), so
        // a read that lands in between can re-cache the old status for one TTL. Milliseconds
        // wide, bounded by the TTL, and harmless if the save then fails — a dropped key only
        // costs a re-read. Closing it properly means invalidating after commit, which needs a
        // domain event; not worth it for an action a human performs a handful of times a year.
        foreach (var key in StoreResolver.CacheKeysFor(store.Slug, store.CustomDomain, settings.Value.BaseDomain))
            await cache.RemoveAsync(key, ct);

        return new StoreResponse(store.Id, store.Name, store.Slug, store.CustomDomain, store.Status.ToString());
    }
}
