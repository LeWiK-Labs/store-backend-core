namespace LeWiK.Store.App.Common.BackOffice;

public sealed class StubBackOfficeClient : IBackOfficeClient
{
    public Task<TenantEntitlement?> GetEntitlementAsync(Guid tenantId, CancellationToken ct = default)
    {
        var entitlement = new TenantEntitlement(
            TenantId: tenantId,
            IsActive: true,
            HasStoreAccess: true,
            PlanCode: "stub-pro");
        return Task.FromResult<TenantEntitlement?>(entitlement);
    }
}