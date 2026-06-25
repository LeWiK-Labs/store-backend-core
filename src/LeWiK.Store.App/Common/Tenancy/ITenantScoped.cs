namespace LeWiK.Store.App.Common.Tenancy;

public interface ITenantScoped
{
    Guid TenantId { get; }
}