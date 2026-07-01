using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Inventory.Domain;

public sealed record StockChanged(
    Guid TenantId,
    Guid ProductVariantId,
    int Available,
    int Reserved) : IDomainEvent;