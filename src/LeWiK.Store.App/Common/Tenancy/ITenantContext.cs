namespace LeWiK.Store.App.Common.Tenancy;

public interface ITenantContext
{
    Guid TenantId { get; }
    bool HasTenant { get; }
}