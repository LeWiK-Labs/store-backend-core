using LeWiK.Store.App.Common.Domain;
using LeWiK.Store.App.Common.Tenancy;

namespace LeWiK.Store.App.Customers.Domain;

public sealed class Customer : Entity, ITenantScoped, IAuditable
{
    public Guid TenantId { get; private init; }
    public string Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string? Name { get; private set; }
    public bool IsRegistered { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    
    private Customer() {} //EF
    
    private Customer(Guid tenantId, string email, string phone, string? name, bool isRegistered)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        Email = email;
        Phone = phone;
        Name = name;
        IsRegistered = isRegistered;
    }

    public static Customer Guest(Guid tenantId, string email, string phone, string? name) =>
        new(tenantId, email, phone, name, isRegistered: false);

    public static Customer Registered(Guid tenantId, string email, string phone, string? name) =>
        new(tenantId, email, phone, name, isRegistered: true);

    // A guest who later registers keeps the same record.
    public void PromoteToRegistered() => IsRegistered = true;

    public void UpdateContact(string phone, string? name)
    {
        Phone = phone;
        Name = name;
    }
}