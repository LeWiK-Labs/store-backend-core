namespace LeWiK.Store.App.Common.BackOffice;

public interface IBackOfficeClient
{
    Task<TenantEntitlement?> GetEntitlementAsync(Guid tenantId, CancellationToken ct = default);
}