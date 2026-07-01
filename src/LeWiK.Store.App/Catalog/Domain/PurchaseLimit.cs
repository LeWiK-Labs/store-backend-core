using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Catalog.Domain;

public enum PurchaseLimitScope { Product, Variant }

public sealed class PurchaseLimit : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public PurchaseLimitScope Scope { get; private init; }
    public Guid TargetId { get; private init; }
    public int? MaxPerOrder { get; private set; }
    public int? MaxPerCustomer { get; private set; }
    public int? WindowDays { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private PurchaseLimit() { } //EF

    public PurchaseLimit(Guid tenantId, PurchaseLimitScope scope, Guid targetId, int? maxPerOrder, int? maxPerCustomer, int? windowDays)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Scope = scope;
        TargetId = targetId;
        Set(maxPerOrder, maxPerCustomer, windowDays);
    }

    public void Set(int? maxPerOrder, int? maxPerCustomer, int? windowDays)
    {
        MaxPerOrder = maxPerOrder;
        MaxPerCustomer = maxPerCustomer;
        WindowDays = maxPerCustomer.HasValue ? windowDays : null;
    }
}