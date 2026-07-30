using LeWiK.Store.App.Common.Domain;

namespace LeWiK.Store.App.Platform.Domain;

public enum StoreStatus { Active, Suspended }

// A tenant. Deliberately NOT ITenantScoped: its Id IS the TenantId every other entity carries,
// so filtering it by tenant would be circular — and platform operators have to see them all.
// Its access control is a policy (4.3), not a query filter.
public sealed class Store : Entity, IAuditable
{
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;          // "cardshop" -> cardshop.tienda.cl
    public string? CustomDomain { get; private set; }          // optional: the store's own domain
    public StoreStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsActive => Status == StoreStatus.Active;

    private Store() { } // EF

    public Store(string name, string slug, string? customDomain = null)
    {
        Id = Guid.CreateVersion7();
        Name = name;
        // Normalised here as well as in the handler: hostnames are case-insensitive, and this
        // is the last place that can guarantee two spellings never become two stores.
        Slug = slug.ToLowerInvariant();
        CustomDomain = customDomain?.ToLowerInvariant();
        Status = StoreStatus.Active;
    }

    public void Rename(string name) => Name = name;
    public void SetCustomDomain(string? domain) => CustomDomain = domain?.ToLowerInvariant();
    public void Suspend() => Status = StoreStatus.Suspended;
    public void Activate() => Status = StoreStatus.Active;
}
