namespace LeWiK.Store.App.Common.Tenancy;

public class TenantContext : ITenantContext
{
    public Guid TenantId  { get; private set; }
    public bool HasTenant => TenantId != Guid.Empty;

    public void SetTenant(Guid tenantId) => TenantId = tenantId;
}