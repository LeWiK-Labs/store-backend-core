namespace LeWiK.Store.App.Common.BackOffice;

public sealed record TenantEntitlement(
    Guid TenantId,
    bool IsActive,
    bool HasStoreAccess,
    string? PlanCode);