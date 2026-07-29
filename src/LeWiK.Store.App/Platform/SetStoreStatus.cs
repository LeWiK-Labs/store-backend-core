using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// ITenantAgnostic: acts ON a store from outside it. The target is the argument, not the
// ambient tenant — an operator suspending a store is never "inside" that store.
public sealed record SetStoreStatusCommand(Guid StoreId, bool Active)
    : ICommand<StoreResponse>, ITenantAgnostic;

public sealed class SetStoreStatusHandler(StoreDbContext db)
    : IRequestHandler<SetStoreStatusCommand, Result<StoreResponse>>
{
    public async Task<Result<StoreResponse>> Handle(SetStoreStatusCommand request, CancellationToken ct)
    {
        var store = await db.Set<Domain.Store>().FirstOrDefaultAsync(s => s.Id == request.StoreId, ct);
        if (store is null) return PlatformErrors.StoreNotFound(request.StoreId);

        if (request.Active) store.Activate(); else store.Suspend();

        return new StoreResponse(store.Id, store.Name, store.Slug, store.CustomDomain, store.Status.ToString());
    }
}
