using LeWiK.Store.App.Common.BackOffice;
using LeWiK.Store.App.Common.Persistence;
using LeWiK.Store.App.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace LeWiK.Store.App.Platform;

// Replaces StubBackOfficeClient: entitlement now comes from the local Store table.
// The interface stays as the seam — if the back-office is ever extracted into its own
// service, only this implementation changes (an HTTP client instead of a query).
public sealed class LocalEntitlementService(StoreDbContext db) : IBackOfficeClient
{
    public async Task<TenantEntitlement?> GetEntitlementAsync(Guid tenantId, CancellationToken ct = default)
    {
        var status = await db.Set<Domain.Store>().AsNoTracking()
            .Where(s => s.Id == tenantId)
            .Select(s => (StoreStatus?)s.Status)
            .FirstOrDefaultAsync(ct);

        // No store, no entitlement. The interface is nullable and callers already treat null
        // as "unknown tenant", which says more than an inactive entitlement for a store that
        // does not exist.
        if (status is null) return null;

        var active = status == StoreStatus.Active;

        // HasStoreAccess tracks IsActive because there is exactly one product to be entitled
        // to, and PlanCode is null because subscription plans are not modelled yet — the stub
        // invented "stub-pro"; reporting nothing is more honest than inventing a tier.
        return new TenantEntitlement(tenantId, active, HasStoreAccess: active, PlanCode: null);
    }
}
