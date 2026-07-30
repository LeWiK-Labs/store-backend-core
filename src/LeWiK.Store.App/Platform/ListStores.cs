using LeWiK.Store.App.Common.Messaging;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Common.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

public sealed record ListStoresQuery() : IQuery<IReadOnlyList<StoreResponse>>;

public sealed class ListStoresHandler(StoreDbContext db)
    : IRequestHandler<ListStoresQuery, Result<IReadOnlyList<StoreResponse>>>
{
    public async Task<Result<IReadOnlyList<StoreResponse>>> Handle(ListStoresQuery request, CancellationToken ct)
    {
        // Store isn't tenant-scoped, so no filter trims this: it returns EVERY store on the
        // platform. That is the intent, and it is exactly why 4.3 has to gate this endpoint.
        var stores = await db.Set<Domain.Store>()
            .OrderBy(s => s.Name)
            .Select(s => new StoreResponse(s.Id, s.Name, s.Slug, s.CustomDomain, s.Status.ToString()))
            .ToListAsync(ct);

        return stores;
    }
}
